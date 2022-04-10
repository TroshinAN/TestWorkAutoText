using System;
using System.ComponentModel;
using System.Linq;
using System.Net.Sockets;
using System.Text;


namespace Managers
{
	/// <summary>
	/// Предоставляет класс работа клиента на сервере.
	/// </summary>
	public class ClientOnServer
	{
		private readonly Socket _socket;
		private readonly SQLManager _sql;
		private readonly BackgroundWorker _worker;

		/// <summary>
		/// Указывает, работает ли клиент на сервере.
		/// </summary>
		public bool IsWorK { get; private set; } = false;
		/// <summary>
		/// Метод-событие для получения сообщений.
		/// </summary>
		public Action<string> Message { get; set; } = delegate { };
		/// <summary>
		/// Метод-событие, оповещающий завершение работы клиента на сервере.
		/// </summary>
		public Action<ClientOnServer> CanDelete { get; set; } = delegate { };

		/// <summary>
		/// Инициализирует новый экземпляр класса работы клиента на сервере.
		/// </summary>
		/// <param name="socket">Сокет клиента, подключенного к серверу.</param>
		/// <param name="sqlManager">Экземпляр Sql менеджера для работы с базой данных.</param>
		public ClientOnServer(Socket socket, SQLManager sqlManager)
		{
			_socket = socket;
			_sql = sqlManager;
			_sql.ErrorMessagge += (message) => { Message(message); };

			_worker = new BackgroundWorker();
			_worker.DoWork += Worker_DoWork;
			_worker.RunWorkerCompleted += Worker_RunWorkerCompleted;
		}

		/// <summary>
		/// Запускает процесс работы клиента на сервере.
		/// </summary>
		public void Process()
		{
			if (!IsWorK)
			{
				_worker.RunWorkerAsync();
			}
		}

		// поток выполнения процесса работы клиента на сервере.
		private void Worker_DoWork(object sender, DoWorkEventArgs e)
		{
			IsWorK = true;
			while (IsWorK && _socket.Connected)
			{
				var messageBuilder = new StringBuilder();
				var data = new byte[1024];
				var isReset = false;

				do
				{
					try
					{
						var bytes = _socket.Receive(data);
						messageBuilder.Append(Encoding.UTF8.GetString(data, 0, bytes));
					}
					catch (SocketException ex)
					{
						isReset = ex.SocketErrorCode == SocketError.ConnectionReset;
					}
					catch
					{ }
				}
				while (_socket.Available > 0);

				if (!isReset && messageBuilder.Length > 0)
				{
					var command = messageBuilder.ToString();

					if (command == "-closed")
					{
						_socket.Disconnect(false);
						continue;
					}

					var messageToSend = ParseCommand(command);

					data = Encoding.UTF8.GetBytes(messageToSend);

					try
					{
						_socket.Send(data);
					}
					catch { }
				}
			}
		}

		private string ParseCommand(string command)
		{
			var @params = command.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

			if (@params.Length == 2)
			{
				if (@params[0] == "get")
				{
					var words = _sql.FindWords(@params[1]);

					if (words != null && words.Count() > 0)
					{
						return string.Join(",", words);
					}
				}
			}

			return " ";
		}

		/// <summary>
		/// Запускает остановку процесса работы клиента на сервере.
		/// </summary>
		public void Stop()
		{
			IsWorK = false;
		}

		// завершение потока выполнения процесса работы клиента на сервере.
		private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			if (IsWorK)
			{
				CanDelete(this);
			}
			_sql.Dispose();
			_socket.Close();
		}
	}
}
