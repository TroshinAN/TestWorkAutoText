using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Managers
{
	/// <summary>
	/// Предоставляет класс для работы сервера.
	/// </summary>
	public class ServerManager : IDisposable
	{
		static readonly Encoding _encoding = Encoding.UTF8;
		private readonly SQLManager _sql;
		private readonly Socket _socket;
		private IPEndPoint _ipPoint;
		private readonly BackgroundWorker _worker;
		private readonly List<ClientOnServer> _clients;
		private readonly ManualResetEvent _acceptResetEvent;

		/// <summary>
		/// Максимальное количество подключаемых клиентов к серверу.
		/// </summary>
		public readonly int MaximumConnection = 100;
		/// <summary> 
		/// Возвращает имя DNS узла.
		/// </summary>
		public string HostName { get; private set; }
		/// <summary>
		/// Указывает, запущен ли сервер в данный момент
		/// </summary>
		public bool IsWorK { get; private set; }
		/// <summary>
		/// Метод-событие для получения сообщений.
		/// </summary>
		public Action<string, bool> Message { get; set; } = delegate { };

		/// <summary>
		/// Инициализирует новый экземпляр класса для работы сервера.
		/// </summary>
		public ServerManager()
		{
			_sql = new SQLManager();
			_sql.ErrorMessagge += (message) => { Message(message, true); };

			_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

			_worker = new BackgroundWorker();
			_worker.DoWork += Worker_DoWork;
			_worker.RunWorkerCompleted += Worker_RunWorkerCompleted;

			_clients = new List<ClientOnServer>();
			_acceptResetEvent = new ManualResetEvent(false);
		}

		/// <summary>
		/// Подключает сервер к базе данных.
		/// </summary>
		/// <param name="connectionString">Строка подключения к SQL серверу.</param>
		/// <returns>Указывает, удалось ли подключиться к SQL серверу.</returns>
		public bool ConnectToServer(string connectionString)
		{
			Message("Подключение к БД\t...\t", false);

			if (_sql.ConnectToServer(connectionString))
			{
				Message("OK", true);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Подключает сервер к файлу базы данных.
		/// </summary>
		/// <param name="pathFile">Путь к файлу базы данных.</param>
		/// <returns>Указывает, удалось ли подключиться к файлу базы данных.</returns>
		public bool ConnectToFile(string pathFile)
		{
			Message("Подключение к БД\t...\t", false);

			if (_sql.ConnectToFile(pathFile))
			{
				Message("OK", true);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Создать узел для подключения клиентов.
		/// </summary>
		/// <param name="port">Порт подключения</param>
		/// <returns>Указывает, удалось ли создать узел для подключения клиентов.</returns>
		public bool CreateConnection(int port)
		{
			HostName = Dns.GetHostName();

			Message("Открытие порта подключения\t...\t", false);
			try
			{
				_ipPoint = new IPEndPoint(IPAddress.Any, port);
				_socket.Bind(_ipPoint);
				_socket.Listen(MaximumConnection);
			}
			catch (Exception ex)
			{
				Message($"Не удалось настроить сервер {HostName}: {ex.Message}", true);
				return false;
			}
			Message("OK", true);

			return true;
		}

		/// <summary>
		/// Выполняет команду по созданию/обновлению словаря в базе данных.
		/// </summary>
		/// <param name="pathFile">Путь с наименованием файла, содержащего произвольный текст.</param>
		/// <param name="isNew">Указывает, какое действие нужно выполнить со словарём.
		/// true - создать новый. false - обновить имеющийся. По умолчанию установлено true.</param>
		/// <returns>Указывает, удалось ли создать/обновить словарь в базе данных.</returns>
		public bool UpdateBaseWordBook(string pathFile, bool isNew = true)
		{
			var context = isNew ? "Загрузка словаря в БД\t...\t" : "Обновление словаря в БД\t...\t";
			Message(context, false);

			if (string.IsNullOrEmpty(pathFile) || !File.Exists(pathFile))
			{
				Console.WriteLine($"Не удалось найти входной файл { pathFile }");
				return false;
			}

			var text = File.ReadAllText(pathFile, _encoding);
			var result = isNew ? _sql.CreateWordBook(text) : _sql.UpdateWordBook(text);

			if (result)
			{
				Message("OK", true);
			}

			return result;
		}

		/// <summary>
		/// Выполняет команду по чистке словаря в базе данных.
		/// </summary>
		/// <returns>Указывает, удалось ли очистить словарь в базе данных.</returns>
		public bool ClearWordBook()
		{
			Message("Очистка словаря в БД\t...\t", false);

			if (_sql.ClearWordBook())
			{
				Message("OK", true);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Запускает сервер.
		/// </summary>
		public void Start()
		{
			if (!IsWorK)
			{
				_worker.RunWorkerAsync();
			}
		}

		// Поток работы сервера.
		private void Worker_DoWork(object sender, DoWorkEventArgs e)
		{
			IsWorK = true;

			while (IsWorK)
			{
				try
				{
					_acceptResetEvent.Reset();
					_socket.BeginAccept(new AsyncCallback(BeginAccept), _socket);
					_acceptResetEvent.WaitOne();
				}
				catch (Exception ex)
				{
					Message($"Ошибка подключения клиента: {ex.Message}.", true);
				}
			}
		}

		private void BeginAccept(IAsyncResult result)
		{
			if (result.AsyncState is Socket socket)
			{
				var socketClient = socket.EndAccept(result);
				var client = new ClientOnServer(socketClient, new SQLManager(_sql.GetNewSqlConnection()));

				client.Process();
				_clients.Add(client);
				client.CanDelete += (deleteClient) =>
				{
					_clients.Remove(deleteClient);
				};
			}
			_acceptResetEvent.Set();
		}

		/// <summary>
		/// Останавливает работу сервера.
		/// </summary>
		public void Stop()
		{
			IsWorK = false;
			_acceptResetEvent.Set();
		}

		// Завершение потока работы сервера.
		private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			foreach (var client in _clients)
			{
				client.Stop();
			}
			_socket.Dispose();
		}

		/// <summary>
		/// Освобождает ресурсы сервера.
		/// </summary>
		public void Dispose()
		{
			Stop();
			_sql.Dispose();
		}
	}
}
