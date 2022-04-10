using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Managers
{
	/// <summary>
	/// Предоставляет класс для работы клиента.
	/// </summary>
	public class ClientManager : IDisposable
	{
		private Socket _socket;
		private IPEndPoint _ipPoint;
		private IPHostEntry _ipHost;
		private readonly ManualResetEvent _resetEvent;

		/// <summary>
		/// Указывает, установлено ли соединение с удалённым узлом.
		/// </summary>
		public bool Connected { get => _socket != null && _socket.Connected; }
		/// <summary>
		/// Возвращает имя DNS узла.
		/// </summary>
		public string HostName { get => _ipHost?.HostName; }
		/// <summary>
		/// Метод-событие для получения сообщений.
		/// </summary>
		public Action<string> Message { get; set; } = delegate { };

		/// <summary>
		/// Инициализирует новый экземпляр класса работы для клиента.
		/// </summary>
		public ClientManager()
		{
			_resetEvent = new ManualResetEvent(false);
		}

		/// <summary>
		/// Устанавливает соединение по указанному адресу и порту.
		/// </summary>
		/// <param name="address">Адрес соединения.</param>
		/// <param name="port">Порт соединения.</param>
		/// <returns>Указывает, удалось ли установить соединение.</returns>
		public bool Connect(string address, int port)
		{
			if (string.IsNullOrWhiteSpace(address))
			{
				Message("Строка адреса не может быть пустой");
				return false;
			}

			try
			{
				_ipHost = Dns.GetHostEntry(address);
			}
			catch (Exception ex)
			{
				Message($"Не удалось определить адрес '{address}': {ex.Message}");
				return false;
			}

			var ipAddress = _ipHost.AddressList.Where(w => w.AddressFamily == AddressFamily.InterNetwork).FirstOrDefault();

			if (ipAddress == null)
			{
				Message($"Не удалось определить IP-адрес у хоста '{address}'");
				return false;
			}

			try
			{
				_ipPoint = new IPEndPoint(ipAddress, port);
			}
			catch (Exception ex)
			{
				Message($"Не удалось установить точку подключения к серверу {HostName}: {ex.Message}");
				return false;
			}

			return Connect();
		}

		private bool Connect()
		{
			if (BeginConnect())
			{
				Message($"Установлено соединение с сервером {HostName}");
				return true;
			}
			else
			{
				return false;
			}
		}

		private bool BeginConnect()
		{
			Close();

			try
			{
				if (_ipPoint == null)
				{
					Message("Точка подключения не установлена. Выполнение команды невозможно.");
					return false;
				}

				_socket = new Socket(_ipPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

				_resetEvent.Reset();
				_socket.BeginConnect(_ipPoint, new AsyncCallback(BeginConnect), _socket);
				_resetEvent.WaitOne();
			}
			catch (Exception ex)
			{
				Message($"Не удалось подключиться к серверу {HostName}: {ex.Message}");
				return false;
			}

			return _socket.Connected;
		}

		private void BeginConnect(IAsyncResult state)
		{
			try
			{
				var s = (Socket)state.AsyncState;
				s.EndConnect(state);
			}
			catch (Exception ex)
			{
				Message($"Не удалось подключиться к серверу {HostName}: {ex.Message}");
			}
			finally
			{
				_resetEvent.Set();
			}
		}

		/// <summary>
		/// Передать сообщение на удалённый узел и принять сообщение в ответ.
		/// </summary>
		/// <param name="message">Сообщение для передачи.</param>
		/// <param name="result">Возвращаемое. Сообщение, принятое с удалённого узнал.</param>
		/// <returns>Указывает, удалось ли выполнить передачу/приём сообщений.</returns>
		public bool TranslateMessage(string message, out string result)
		{
			result = string.Empty;
			return CheckConnect() && SendToServer(message) && ReceiveFromServer(ref result);
		}

		// Отправить сообщение
		private bool SendToServer(string message)
		{
			try
			{
				var sendData = Encoding.UTF8.GetBytes(message);
				_socket.Send(sendData);
			}
			catch (Exception ex)
			{
				Message($"Ошибка передачи данных на сервер: {ex.Message}");
				return false;
			}

			return true;
		}

		// Принять сообщение
		private bool ReceiveFromServer(ref string result)
		{
			byte[] data;
			StringBuilder messageFromServer = new StringBuilder();

			do
			{
				int bytes = 0;
				data = new byte[1024];

				try
				{
					bytes = _socket.Receive(data, data.Length, SocketFlags.None);
				}
				catch (SocketException ex)
				{
					if (ex.SocketErrorCode != SocketError.TimedOut)
					{
						Message($"Ошибка приёма данных с сервера: {ex.Message}");
						return false;
					}
				}
				catch (Exception ex)
				{
					Message($"Ошибка приёма данных с сервера: {ex.Message}");
					return false;
				}

				messageFromServer.Append(Encoding.UTF8.GetString(data, 0, bytes));

			} while (_socket.Available > 0);

			result = messageFromServer.ToString();

			return true;
		}

		// Проверить соединение. При отсутствии произвести переподключение.
		private bool CheckConnect()
		{
			if (!Connected)
			{
				BeginConnect();
			}
			return Connected;
		}

		/// <summary>
		/// Закрыть соединение с удалённым узлом.
		/// </summary>
		public void Close()
		{
			if (_socket != null && Connected)
			{
				SendToServer("-closed");
				Thread.Sleep(500);
				_socket.Shutdown(SocketShutdown.Both);
				_socket.Close();
			}
		}

		/// <summary>
		/// Освобождает все используемые ресурсы.
		/// </summary>
		public void Dispose()
		{
			_socket.Dispose();
		}
	}
}
