using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;


namespace Managers
{
	/// <summary>
	/// Предоставляет класс работа с базой данных MSSQL.
	/// </summary>
	public class SQLManager : IDisposable
	{
		private SqlConnection _sqlConnection;
		private const string _defaultConnetionString = @"Data Source=(localdb)\MSSQLLocalDB;Integrated Security=True";
		private const string _defaultDataBaseName = "TroshinTestWorkAutoText";

		/// <summary>
		/// Минимальная длина слова для загрузки в БД.
		/// </summary>
		public const int WordMinLength = 3;
		/// <summary>
		/// Максимальная длина слова для загрузки в БД.
		/// </summary>
		public const int WordMaxLength = 15;
		/// <summary>
		/// Наименование таблицы - словаря в БД.
		/// </summary>
		public const string DataBaseTableName = "WordBook";
		/// <summary>
		/// Наименование процедуры в БД для получения совпадений.
		/// </summary>
		public const string DataBaseGetWordsProcedureName = "GetLikeWords";
		/// <summary>
		/// Получает наименование базы данных.
		/// </summary>
		public string DataBaseName { get; private set; }
		/// <summary>
		/// Получает строку подключения к базе данных.
		/// </summary>
		public static string ConnectionString { get; private set; }

		/// <summary>
		/// Событие на получение текста ошибки.
		/// </summary>
		public Action<string> ErrorMessagge { get; set; } = delegate { };

		public SQLManager()
		{

		}

		internal SQLManager(SqlConnection sqlConnection)
		{
			_sqlConnection = sqlConnection;
		}

		/// <summary>
		/// Процедура подключения к базе данных.
		/// </summary>
		/// <param name="connectionString">Строка подключения к базе данных.</param>
		/// <returns>Указывает, удалось ли подключиться к базе данных.</returns>
		public bool ConnectToServer(string connectionString = _defaultConnetionString)
		{
			var regex = new Regex(@"Initial Catalog=(?<Catalog>(\w+));?");
			var match = regex.Match(connectionString);

			// Проверяем, имеет ли строка подключение базу данных.
			// Если имеет, то запускаем только проверку на наличие необходимых таблиц и процедур в БД и подключаемся к ней.
			// Если нет, то осуществляем полную проверку/создание БД и подключение к ней.
			if (match.Success)
			{
				DataBaseName = match.Groups["Catalog"].Value;
				ConnectionString = connectionString;
				return CheckExistsDBElements();
			}
			else
			{
				DataBaseName = _defaultDataBaseName;
				return CheckExistsDataBase(connectionString);
			}
		}

		/// <summary>
		/// Процедура присоединения файла данных к базе данных.
		/// </summary>
		/// <param name="fileDB">Путь с наименованием файла базы данных.</param>
		/// <returns>Указывает, удалось ли присоединить файл к базе данных.</returns>
		public bool ConnectToFile(string fileDB)
		{
			if (!File.Exists(fileDB))
			{
				ErrorMessagge($"Не удалось найти файл базы данных: {fileDB}");
				return false;
			}

			fileDB = Directory.Exists(Path.GetDirectoryName(fileDB)) ? fileDB : $"{Directory.GetCurrentDirectory()}\\{fileDB}";

			DataBaseName = _defaultDataBaseName;
			return UpdateFilesDataBase(fileDB);
		}

		/// <summary>
		/// Создаёт новый словарь частовстречаемых слов в базе данных.
		/// </summary>
		/// <param name="text">Текст, из которого будет сформирован словарь.</param>
		/// <returns>Указывает, удалось ли сформировать словарь частовстречаемых слов.</returns>
		public bool CreateWordBook(string text)
		{
			var words = GetWordsFromText(text);
			var wordGroups = GetWordGroups(words);

			if (wordGroups?.Count() > 0)
			{
				if (ClearWordBook())
				{
					var cmdSql = GetCmdForInsert(DataBaseTableName, wordGroups);

					try
					{
						var cmd = new SqlCommand(cmdSql, _sqlConnection);
						cmd.ExecuteNonQuery();
					}
					catch (Exception ex)
					{
						ErrorMessagge(string.Format("Не удалось создать словарь\n{0}", ex.Message));
						return false;
					}
				}
				else
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Обновляет имеющийся словарь частовстречаемых слов в базе данных.
		/// </summary>
		/// <param name="text">Текст, из которого будет сформирован словарь.</param>
		/// <returns>Указывает, удалось ли обновить словарь частовстречаемых слов.</returns>
		public bool UpdateWordBook(string text)
		{
			var words = GetWordsFromText(text);
			var wordGroups = GetWordGroups(words);

			if (wordGroups?.Count() > 0)
			{
				// Создадим временную таблицу в БД, для записи в неё новых слов и сравнения с имеющимися на текущий момент в словаре.
				var tmpTableName = "#tmpWordBook";
				var transaction = _sqlConnection.BeginTransaction();
				var cmd = new SqlCommand
				{
					Connection = _sqlConnection,
					Transaction = transaction
				};

				try
				{
					cmd.CommandText = $"CREATE TABLE {tmpTableName} (Word VARCHAR({WordMaxLength}) COLLATE Cyrillic_General_CS_AS, Frequency INT) ";
					cmd.ExecuteNonQuery();
					cmd.CommandText = GetCmdForInsert(tmpTableName, wordGroups);
					cmd.ExecuteNonQuery();
					cmd.CommandText = GetCmdForUpdate(tmpTableName);
					cmd.ExecuteNonQuery();
					cmd.CommandText = $"DROP TABLE {tmpTableName}";
					cmd.ExecuteNonQuery();

					transaction.Commit();
				}
				catch (Exception ex)
				{
					ErrorMessagge(string.Format("Не удалось обновить словарь\n{0}", ex.Message));
					transaction.Rollback();
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Очищает имеющийся словарь частовстречаемых слов в базе данных.
		/// </summary>
		/// <returns>Указывает, удалось ли очистить словарь частовстречаемых слов.</returns>
		public bool ClearWordBook()
		{
			var sqlExpression = $"TRUNCATE TABLE {DataBaseTableName}";

			try
			{
				var cmd = new SqlCommand(sqlExpression, _sqlConnection);
				cmd.ExecuteNonQuery();
			}
			catch (Exception ex)
			{
				ErrorMessagge(string.Format("Не удалось очистить словарь\n{0}", ex.Message));
				return false;
			}
			return true;
		}

		/// <summary>
		/// Находит варианты слов по указанной части слова.
		/// </summary>
		/// <param name="word">Часть слова для поиска совпадений в словаре.</param>
		/// <param name="top">Опционально. Количество предложенных вариантов слов.</param>
		/// <returns>Возвращает список найденных слов.</returns>
		public IEnumerable<string> FindWords(string word, int top = 5)
		{
			if (!string.IsNullOrEmpty(word) && top >= 0)
			{
				var sqlExpression = $"EXEC dbo.GetLikeWords '{word}', {top}";
				var cmd = new SqlCommand(sqlExpression, _sqlConnection);

				try
				{
					var reader = cmd.ExecuteReader();
					var result = new List<string>();

					if (reader.HasRows)
					{
						while (reader.Read())
						{
							result.Add(reader.GetString(0));
						}
					}

					reader.Close();
					return result;
				}
				catch (Exception ex)
				{
					ErrorMessagge(string.Format("Не удалось найти совпадения в словаре\n{0}", ex.Message));
					return null;
				}
			}
			return null;
		}

		/// <summary>
		/// Получает новое соединение с базой данных по текущей строке подключения.
		/// </summary>
		/// <returns>Возвращает экземпляр нового соединения с базой данных.</returns>
		public SqlConnection GetNewSqlConnection()
		{
			var newSql = new SqlConnection(ConnectionString);
			newSql.Open();
			return newSql;
		}

		/// <summary>
		/// Получает команду SQL для вставки данных в словарь.
		/// </summary>
		/// <param name="tableName">Наименование таблицы.</param>
		/// <param name="groups">Сгруппированные по частоте слова для вставки в словарь.</param>
		/// <returns>Указывает, удалось ли вставить данные в словарь.</returns>
		private string GetCmdForInsert(string tableName, IEnumerable<IGrouping<string, string>> groups)
		{
			var stringBuilder = new StringBuilder($"INSERT INTO {tableName} (Word, Frequency) VALUES ");
			var isFirst = true;

			foreach (var group in groups)
			{
				if (isFirst)
				{
					stringBuilder.AppendLine($"('{group.Key}', {group.Count()})");
					isFirst = false;
				}
				else
				{
					stringBuilder.AppendLine($",('{group.Key}', {group.Count()})");
				}
			}

			return stringBuilder.ToString();
		}

		/// <summary>
		/// Получает команду SQL для обновления данных в словаре.
		/// </summary>
		/// <param name="tempTableName">Наименование таблицы с данными для обновления.</param>
		/// <returns></returns>
		private string GetCmdForUpdate(string tempTableName)
		{
			return $@"
UPDATE wb
SET wb.Frequency = wb.Frequency + twb.Frequency
FROM {DataBaseTableName} wb
	JOIN {tempTableName} twb ON LOWER(twb.Word) = LOWER(wb.Word)

INSERT INTO {DataBaseTableName} (Word, Frequency)
SELECT LOWER(twb.Word), twb.Frequency
FROM {tempTableName} twb
WHERE NOT EXISTS (SELECT 1 FROM {DataBaseTableName} wb WHERE LOWER(wb.Word) = LOWER(twb.Word))
";
		}

		/// <summary>
		/// Группирует список слов по частоте.
		/// </summary>
		/// <param name="words">Список слов.</param>
		/// <returns>Возвращает сгруппированный список слов по частоте.</returns>
		private IEnumerable<IGrouping<string, string>> GetWordGroups(IEnumerable<string> words)
		{
			if (words?.Count() > 0)
			{
				// Выбираем все слова, длиной от указанной минимальной длины до указанной максимальной длины, которые встречаются не менее 3х раз.
				return words.GroupBy(gr => gr).Where(w => w.Key.Length >= WordMinLength && w.Key.Length <= WordMaxLength && w.Count() >= 3);
			}

			return null;
		}

		/// <summary>
		/// Получает список слов из текста.
		/// </summary>
		/// <param name="text">Текст.</param>
		/// <returns>Возвращает список слов из текста.</returns>
		private IEnumerable<string> GetWordsFromText(string text)
		{
			if (!string.IsNullOrEmpty(text))
			{
				var result = new List<string>();
				var regex = new Regex(@"\b[A-zА-я]{3,15}\b");
				var matches = regex.Matches(text.ToLower());

				// Выбираем все слова из произвольного текста.
				foreach (Match match in matches)
				{
					result.Add(match.Value);
				}

				return result;
			}

			return null;
		}

		/// <summary>
		/// Проверяет наличие базы данных в указанном подключении.
		/// Если база данных отсутствует - она создаётся.
		/// Вместе с базой данных создаются и необходимые таблицы и процедуры.
		/// </summary>
		/// <param name="connectionString">Строка подключения к серверу SQL.</param>
		/// <returns>Указывает, удалось ли провести проверку наличия БД и её необходимых компонентов.</returns>
		private bool CheckExistsDataBase(string connectionString)
		{
			var sqlExpression = $@"
IF NOT EXISTS(SELECT 1 FROM master.dbo.sysdatabases WHERE name = '{DataBaseName}')
BEGIN
	CREATE DATABASE {DataBaseName}
	ON
	(
		 NAME = {DataBaseName}_dat
		,FILENAME = '{Directory.GetCurrentDirectory()}\{DataBaseName}.mdf'
	)
	COLLATE Cyrillic_General_CS_AS;
END;";

			return ExecCheckExistsDataBase(sqlExpression, connectionString);
		}

		/// <summary>
		/// Подключает указанный файл к базе данных.
		/// Если база данных отсутствует - она создаётся.
		/// Вместе с базой данных создаются и необходимые таблицы и процедуры.
		/// </summary>
		/// <param name="fileDB">Путь с наименованием файла базы данных.</param>
		/// <returns>Указывает, удалось ли подключить файл данных к базе.</returns>
		private bool UpdateFilesDataBase(string fileDB)
		{
			var sqlExpression = $@"
IF NOT EXISTS(SELECT 1 FROM master.dbo.sysdatabases WHERE name = '{DataBaseName}')
BEGIN
	CREATE DATABASE {DataBaseName}
	ON
	(
		 NAME = {DataBaseName}_dat    	
		,FILENAME = '{fileDB}'
	)
	FOR ATTACH;
END
ELSE
BEGIN
	ALTER DATABASE {DataBaseName}
	MODIFY FILE
	(
		 NAME = {DataBaseName}_dat
		,FILENAME = '{fileDB}'
	);
END;";

			return ExecCheckExistsDataBase(sqlExpression, _defaultConnetionString);
		}

		/// <summary>
		/// Проверяет наличие базы данных по указанному запросу SQL.
		/// Если база данных отсутствует - она создаётся.
		/// Вместе с базой данных создаются и необходимые таблицы и процедуры.
		/// </summary>
		/// <param name="expresstion">Запрос SQL на проверку/создание базы данных.</param>
		/// <param name="connectionString">Строка подключения к серверу SQL/</param>
		/// <returns>Указывает, удалось ли провести проверку наличия БД и её необходимых компонентов.</returns>
		private bool ExecCheckExistsDataBase(string expresstion, string connectionString)
		{
			var result = true;

			try
			{
				var sqlConnection = new SqlConnection(connectionString);
				sqlConnection.Open();

				var cmd = new SqlCommand(expresstion, sqlConnection);
				cmd.ExecuteNonQuery();
				sqlConnection.Close();

				ConnectionString = $"{connectionString};Initial Catalog={DataBaseName}";
				result = CheckExistsDBElements();
			}
			catch (Exception ex)
			{
				ErrorMessagge($"Не удалось Создать БД\n{ex.Message}");
				result = false;
			}


			return result;
		}

		/// <summary>
		/// Создаёт необходимые таблицы и процедуры в базе данных, а так же осуществляет подключение к ней.
		/// </summary>
		/// <returns>Указывает, удалось ли подключиться к базе данных.</returns>
		private bool CheckExistsDBElements()
		{
			var sqlExpression = $@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '{DataBaseTableName}')
BEGIN
	CREATE TABLE {DataBaseTableName}
	(
		  Id INT NOT NULL PRIMARY KEY IDENTITY
		 ,Word VARCHAR({WordMaxLength}) NOT NULL
		 ,Frequency INT NOT NULL
	)
END;

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = '{DataBaseGetWordsProcedureName}' AND type = 'P ')
BEGIN

EXEC ('
CREATE PROCEDURE dbo.{DataBaseGetWordsProcedureName}
	 @text VARCHAR(MAX)
	,@top INT = 0
AS
	
SET ROWCOUNT @top

SELECT LOWER(Word) word
FROM {DataBaseTableName}
WHERE LOWER(Word) LIKE (LOWER(@text) + ''%'')
ORDER BY Frequency DESC, LOWER(Word)

SET ROWCOUNT 0
')

END;
";

			_sqlConnection = new SqlConnection(ConnectionString);

			try
			{
				_sqlConnection.Open();
				var cmd = new SqlCommand(sqlExpression, _sqlConnection);
				cmd.ExecuteNonQuery();
			}
			catch (Exception ex)
			{
				Dispose();
				ErrorMessagge($"Не удалось подключиться к БД\n{ex.Message}");
				return false;
			}

			return true;
		}

		/// <summary>
		/// Производит освобождение ресурсов, в т.ч. отключение от базы данных.
		/// </summary>
		public void Dispose()
		{
			if (_sqlConnection?.State == System.Data.ConnectionState.Open)
			{
				_sqlConnection.Close();
			}
		}
	}
}
