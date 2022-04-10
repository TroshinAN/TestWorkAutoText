using System;
using System.Collections.Generic;
using Managers;

namespace Server
{
	internal class ServerProgram
	{
		private static ServerManager _server;

		private static void Main(string[] args)
		{
			// Для отработки - имитация входных параметров.
			//args = new string[] { "-s", @"Data Source=(localdb)\MSSQLLocalDB;Integrated Security=True", "7273" };
			//args = new string[] { "-f", @"TroshinTestWorkAutoText.mdf", "7273" };

			_server = new ServerManager();
			_server.Message += (message, isLine) =>
			{
				if (isLine)
				{
					Console.WriteLine(message);
				}
				else
				{
					Console.Write(message);
				}
			};

			if (CheckParameters(args))
			{
				Start();
			}
			else
			{
				Pause();
			}
		}

		private static void Start()
		{
			_server.Start();

			bool isProgress = true;

			while (isProgress)
			{
				var line = Console.ReadLine().Trim().ToLower();
				var @params = GetParamsFromString(line);

				if (@params != null && @params.Length > 0)
				{
					switch (@params[0])
					{
						case "-i":
							InsertToWordBook(@params);
							break;
						case "-u":
							InsertToWordBook(@params, false);
							break;
						case "-d":
							ClearWordBook(@params);
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

		private static void InsertToWordBook(string[] @params, bool isNew = true)
		{
			if (@params.Length == 2)
			{
				_server.UpdateBaseWordBook(@params[1], isNew);
			}
			else
			{
				Console.WriteLine($"Неверное число параметров команды {@params[0]}.");
			}
		}

		private static void ClearWordBook(string[] @params)
		{
			if (@params.Length == 1)
			{
				_server.ClearWordBook();
			}
			else
			{
				Console.WriteLine($"Неверное число параметров команды{@params[0]}.");
			}
		}

		private static bool CheckParameters(string[] args)
		{
			if (args.Length == 3)
			{
				if (string.IsNullOrWhiteSpace(args[0]) || string.IsNullOrWhiteSpace(args[1]) || string.IsNullOrWhiteSpace(args[2]))
				{
					Console.WriteLine("Один из входных параметров пуст.");
					return false;
				}

				return TryConnect(args[0], args[1], args[2]);
			}
			else
			{
				Console.WriteLine("Неверное число входных параметров.");
				return false;
			}
		}

		private static bool TryConnect(string command, string connection, string port)
		{
			if (!int.TryParse(port, out int iPort))
			{
				Console.WriteLine($"Не удалось определить порт подключения: {port}.");
				return false;
			}

			bool result;
			switch (command)
			{
				case "-s":
					result = _server.ConnectToServer(connection);
					break;
				case "-f":
					result = _server.ConnectToFile(connection);
					break;

				default:
					Console.WriteLine($"Неизвестная команда '{command}'.");
					result = false;
					break;
			}

			if (result)
			{
				result = _server.CreateConnection(iPort);
			}

			return result;
		}

		private static string[] GetParamsFromString(string value)
		{
			var result = new List<string>();

			if (!string.IsNullOrWhiteSpace(value))
			{
				while (value.Length > 0)
				{
					value = value.Trim();

					string param;
					if (value.StartsWith("\""))
					{
						var lastIndex = value.IndexOf('\"', 1);

						if (lastIndex == -1)
						{
							Console.WriteLine("Неверный формат команды");
							return null;
						}

						param = value.Substring(1, lastIndex - 1);
						value = value.Remove(0, param.Length + 2);
					}
					else
					{
						var lastIndex = value.IndexOf(' ');

						if (lastIndex == -1)
						{
							param = value;
						}
						else
						{
							param = value.Substring(0, lastIndex);
						}
						value = value.Remove(0, param.Length);
					}

					result.Add(param);
				}
			}

			return result.ToArray();
		}



		private static void Pause()
		{
			Console.Write("Для выхода из программы нажмите любую клавишу...");
			Console.ReadKey(true);
		}
	}
}
