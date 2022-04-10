using System;
using Managers;

namespace Client
{
	internal class ClientProgram
	{
		private static ClientManager _client;

		private static void Main(string[] args)
		{
			// Для отработки - имитация входных параметров.
			// args = new string[] { Dns.GetHostName(), "7273" };

			var isChecked = true;

			_client = new ClientManager();
			_client.Message += (message) => { Console.WriteLine(message); };

			if (args.Length > 0)
			{
				isChecked = CheckParameters(args);
			}

			if (isChecked)
			{
				Start();
				_client.Close();
			}
			else
			{
				Pause();
			}
		}

		private static void Start()
		{
			bool isProgress = true;

			while (isProgress)
			{
				Console.Write("Введите команду: ");
				var line = Console.ReadLine().Trim().ToLower();
				var @params = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

				if (@params.Length > 0)
				{
					switch (@params[0])
					{
						case "connect":
							ParseConnect(@params);
							break;
						case "get":
							ParseGet(@params);
							break;
						case "exit":
							isProgress = false;
							break;

						default:
							Console.WriteLine($"Неизвестная команда '{@params[0]}' в строке '{line}'.");
							break;
					}
				}
			}
		}

		private static void ParseGet(string[] @params)
		{
			if (@params.Length == 2)
			{
				TranslateCommand(@params);
			}
			else
			{
				Console.WriteLine($"Неверное число параметров команды {@params[0]}.");
			}
		}

		private static void TranslateCommand(string[] values)
		{
			if (values != null && values.Length > 0)
			{
				var command = string.Join(" ", values);

				if (_client.TranslateMessage(command, out string result))
				{
					if (!string.IsNullOrWhiteSpace(result))
					{
						var variants = result.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

						foreach (var variant in variants)
						{
							Console.WriteLine($"- {variant}");
						}
					}
				}
			}
		}

		private static void ParseConnect(string[] @params)
		{
			if (@params.Length == 3)
			{
				TryConnect(@params[1], @params[2]);
			}
			else
			{
				Console.WriteLine($"Неверное число параметров команды {@params[0]}.");
			}
		}

		private static bool CheckParameters(string[] args)
		{
			if (args.Length == 2)
			{
				if (string.IsNullOrWhiteSpace(args[0]) || string.IsNullOrWhiteSpace(args[0]))
				{
					Console.WriteLine("Один из входных параметров пуст.");
					return false;
				}

				return TryConnect(args[0], args[1]);
			}
			else
			{
				Console.WriteLine("Неверное число входных параметров.");
				return false;
			}
		}

		private static bool TryConnect(string address, string port)
		{
			if (!int.TryParse(port, out int iPort))
			{
				Console.WriteLine($"Не удалось определить порт подключения: {port}.");
				return false;
			}

			return _client.Connect(address, iPort);
		}

		private static void Pause()
		{
			Console.Write("Для выхода из программы нажмите любую клавишу...");
			Console.ReadKey(true);
		}
	}
}
