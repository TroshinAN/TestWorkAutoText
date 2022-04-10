using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Managers;

namespace TestWorkAutoText
{
	class Program
	{
		static readonly Encoding encoding = Encoding.UTF8;
		static readonly StringBuilder lastString = new StringBuilder();
		static readonly StringBuilder header = new StringBuilder();
		static SQLManager sql;

		static void Main(string[] args)
		{
			// Для отработки - имитация входных параметров.
			//args = new string[] { "-i", "WordBook.txt" };
			Console.Clear();

			// Подключение к БД.
			var isChecked = CheckSQLConnection();

			if (isChecked && args.Length > 0)
			{
				// Выполнение команд входных параметров.
				isChecked = CheckParameters(args);
			}

			if (isChecked)
			{
				//Console.WriteLine();
				StringBuilder str = new StringBuilder();
				bool isProgress = true;

				do
				{
					var c = Console.ReadKey(true);

					// Для разбора слов считываем только буквенные значения.
					switch (c.Key)
					{
						// При нажатии ESC выходим из режима считывания.
						case ConsoleKey.Escape:
							isProgress = false;
							break;
						// Стереть последний введённый символ.
						case ConsoleKey.Backspace:
							if (str.Length > 0)
							{
								str.Remove(str.Length - 1, 1);
								lastString.Remove(lastString.Length - 1, 1);
								ViewHeader();
							}
							break;
						// Разбор введённого слова.
						case ConsoleKey.Enter:
							var sendString = str.ToString();

							if (string.IsNullOrEmpty(sendString))
							{
								isProgress = false;
								break;
							}

							// получаем список найденных слов по введённой части слова.
							var words = sql.FindWords(sendString);

							if (words?.Count() > 0)
							{
								var word = SelectWord(words, sendString);
								header.AppendLine(word);
							}
							else
							{
								header.AppendLine(sendString);
							}

							lastString.Clear();
							str.Clear();
							ViewHeader();
							break;

						default:
							// Если ввели буквенное значение.
							if (char.IsLetter(c.KeyChar))
							{
								str.Append(c.KeyChar);
								lastString.Append(c.KeyChar);
								ViewHeader();
							}
							break;
					}
				}
				while (isProgress);
			}

			// Отключаемся от сервера SQL.
			sql.Dispose();
			return;
		}

		/// <summary>
		/// Предоставляет пользователю выбор необходимого слова из списка вариантов.
		/// </summary>
		/// <param name="words">Список вариантов слов.</param>
		/// <returns>Возвращает выбранное пользователем слово.</returns>
		private static string SelectWord(IEnumerable<string> words, string defaultWord)
		{
			Console.WriteLine();
			int i = 1;

			// Отображаем пользователю слова на выбор
			foreach (var word in words)
			{
				Console.WriteLine($"{i++}. {word}");
			}

			Console.WriteLine();
			Console.WriteLine("0. Назад");

			// Предоставляем возможность выбрать пользователю необходимое слово. Выбор обязателен.
			while (true)
			{
				var c = Console.ReadKey(true);
				switch (c.Key)
				{
					case ConsoleKey.D1:
					case ConsoleKey.D2:
					case ConsoleKey.D3:
					case ConsoleKey.D4:
					case ConsoleKey.D5:
						var index = Convert.ToInt32(c.KeyChar.ToString());
						if (index <= words.Count())
						{
							return words.ElementAt(index - 1);
						}
						break;
					case ConsoleKey.D0:
						return defaultWord;
				}
			}
		}

		/// <summary>
		/// Осуществляет подключение к базе данных.
		/// </summary>
		/// <returns>Указывает, удалось ли подключиться к базе данных.</returns>
		private static bool CheckSQLConnection()
		{
			var context = "Подключение к БД\t...\t";
			sql = new SQLManager();
			sql.ErrorMessagge += (message) => { Console.WriteLine(message); };

			Console.Write(context);
			header.Append(context);

			if (sql.ConnectToServer())
			{
				Console.WriteLine("OK");
				header.AppendLine("OK");
				return true;
			}

			return false;
		}

		/// <summary>
		/// Выполнение команд входных параметров программы.
		/// </summary>
		/// <param name="args">Входные параметры программы.</param>
		/// <returns>Указывает, удалось ли выполнить команду по входным параметрам.</returns>
		private static bool CheckParameters(string[] args)
		{
			if (args.Length == 2)
			{
				switch (args[0])
				{
					case "-i":
						return UpdateBaseWordBook(args[1]);
					case "-u":
						return UpdateBaseWordBook(args[1], false);

					default:
						Console.WriteLine($"Неверный входной параметр { args[0] }");
						return false;
				}
			}
			else if (args.Length == 1)
			{
				if (args[0] == "-d")
				{
					return ClearWordBook();
				}
				else
				{
					Console.WriteLine($"Неверный входной параметр { args[0] }");
					return false;
				}
			}
			else
			{
				Console.WriteLine("Неверное число параметров.");
				return false;

			}
		}

		/// <summary>
		/// Выполняет команду по созданию/обновлению словаря в базе данных.
		/// </summary>
		/// <param name="pathFile">Путь с наименованием файла, содержащего произвольный текст.</param>
		/// <param name="isNew">Указывает, какое действие нужно выполнить со словарём.
		/// true - создать новый. false - обновить имеющийся. По умолчанию установлено true.</param>
		/// <returns>Указывает, удалось ли создать/обновить словарь в базе данных.</returns>
		private static bool UpdateBaseWordBook(string pathFile, bool isNew = true)
		{
			var context = isNew ? "Загрузка словаря в БД\t...\t" : "Обновление словаря в БД\t...\t";
			Console.Write(context);
			header.Append(context);

			if (string.IsNullOrEmpty(pathFile) || !File.Exists(pathFile))
			{
				Console.WriteLine($"Не удалось найти входной файл { pathFile }");
				return false;
			}

			var text = File.ReadAllText(pathFile, encoding);
			var result = isNew ? sql.CreateWordBook(text) : sql.UpdateWordBook(text);

			if (result)
			{
				Console.WriteLine("OK");
				header.AppendLine("OK");
			}

			return result;
		}

		/// <summary>
		/// Выполняет команду по чистке словаря в базе данных.
		/// </summary>
		/// <returns>Указывает, удалось ли очистить словарь в базе данных.</returns>
		private static bool ClearWordBook()
		{
			header.Append("Очистка словаря в БД\t...\t");

			if (sql.ClearWordBook())
			{
				header.AppendLine("OK");
				return true;
			}

			return false;
		}

		private static void ViewHeader()
		{
			Console.Clear();
			Console.WriteLine(header.ToString());
			Console.Write(lastString.ToString());
		}
	}
}
