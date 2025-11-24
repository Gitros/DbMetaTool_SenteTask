using DbMetaTool.Application.Contracts;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Isql;

namespace DbMetaTool.Application.Services;

/// <summary>
/// Odpowiada za tworzenie nowych baz Firebird,
/// inicjalizowanie ich skryptami SQL oraz aktualizowanie istniejących baz
/// </summary>
internal class DatabaseManager : IDatabaseManager
{
    private const int DefaultPageSize = 8192;
    private const string DefaultDatabaseName = "database.fdb";

    /// <summary>
    /// Tworzy nową bazę danych Firebird w podanym katalogu.
    /// Jeśli katalog nie istnieje – zostanie automatycznie utworzony.
    /// </summary>
    /// <param name="databaseDirectory">Katalog, w którym ma zostać utworzony plik .fdb.</param>
    /// <returns>Pełna ścieżka do utworzonej bazy danych.</returns>
    public string CreateDatabase(string databaseDirectory)
    {
        Console.WriteLine("== Creating Firebird database ==");

        var dbPath = ValidateOrCreateDatabaseDirectory(databaseDirectory);

        if(File.Exists(dbPath))
            throw new InvalidOperationException($"Database already exists: {databaseDirectory}");

        Console.WriteLine($"Creating database at: {dbPath}");
        var connectionString = BuildConnectionString(dbPath);
        FbConnection.CreateDatabase(connectionString, DefaultPageSize, true, true);

        return dbPath;
    }

    public void ExecuteDatabaseScriptsWithDatabasePath(string databasePath, string scriptsDirectory)
    {
        Console.WriteLine("== Executing scripts ==");
        using var context = GetDatabaseContextByDatabasePath(databasePath);
        
        var scripts = LoadScriptFiles(scriptsDirectory);

        ExecuteScriptFiles(context, scripts);
    }

    /// <summary>
    /// Aktualizuje istniejącą bazę danych Firebird na podstawie katalogu ze skryptami.
    /// Działa w sposób permisywny – dodaje nowe obiekty, ale nie modyfikuje istniejących.
    /// Istniejące domeny i tabele są pomijane, aby uniknąć konfliktów i migracji danych.
    /// Procedury i pozostałe skrypty są wykonywane normalnie.
    /// </summary>
    /// <param name="connectionString">Connection string do istniejącej bazy.</param>
    /// <param name="scriptsDirectory">Katalog ze skryptami SQL.</param>
    public void ExecuteDatabaseScriptsWithConnectionString(string connectionString, string scriptsDirectory)
    {
        Console.WriteLine("== Updating existing database ==");
        Console.WriteLine($"Using connection: {connectionString}");
        Console.WriteLine();

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be empty.", nameof(connectionString));

        if (string.IsNullOrWhiteSpace(scriptsDirectory))
            throw new ArgumentException("Scripts directory cannot be empty.", nameof(scriptsDirectory));

        using var context = new FbConnection(connectionString);
        context.Open();
        Console.WriteLine("Connected to database.");

        var scripts = LoadScriptFiles(scriptsDirectory);

        var existingDomains = LoadExistingDomains(context);
        var existingTables = LoadExistingTables(context);

        foreach (var scriptFile in scripts)
        {
            if (IsSourcedFromDomainsFolder(scriptFile))
            {
                var domainName = Path.GetFileNameWithoutExtension(scriptFile);
                if (existingDomains.Contains(domainName))
                {
                    Console.WriteLine($"Skipping domain {domainName} (already exists).");
                    continue;
                }

                Console.WriteLine($"Applying new domain: {domainName}");
            }

            if (IsSourcedFromTablesFolder(scriptFile))
            {
                var tableName = Path.GetFileNameWithoutExtension(scriptFile);
                if (existingTables.Contains(tableName))
                {
                    Console.WriteLine($"Skipping table {tableName} (already exists).");
                    continue;
                }

                Console.WriteLine($"Creating new table: {tableName}");
            }

            ExecuteScriptFile(context, scriptFile);
        }
    }

    /// <summary>
    /// Tworzy katalog na bazę danych
    /// </summary>
    private string ValidateOrCreateDatabaseDirectory(string databaseDirectory)
    {
        var dbDirectory = Path.GetFullPath(databaseDirectory);

        if (!Directory.Exists(dbDirectory))
        {
            Directory.CreateDirectory(dbDirectory);
            Console.WriteLine($"Created directory {dbDirectory}");
        }

        var dbPath = Path.Combine(dbDirectory, DefaultDatabaseName);

        return dbPath;
    }

    /// <summary>
    /// Buduje connection string do lokalnej instancji Firebirda.
    /// </summary>
    private static string BuildConnectionString(string databasePath)
    {
        return new FbConnectionStringBuilder
        {
            Database = databasePath,
            DataSource = "localhost",
            Port = 3050,
            UserID = "SYSDBA",
            Password = "masterkey",
            ServerType = FbServerType.Default,
            Charset = "UTF8",
            Pooling = false,
        }.ToString();
    }

    /// <summary>
    /// Otwiera połączenie do bazy na podstawie ścieżki do pliku.
    /// </summary>
    private FbConnection GetDatabaseContextByDatabasePath(string databasePath)
    {
        var connectionString = BuildConnectionString(databasePath);
        var connection = new FbConnection(connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Wczytuje wszystkie pliki .sql z katalogu skryptów
    /// Sortuje pliki w sposób zapobiegający problemom z ograniczeniami zależności
    /// </summary>
    internal List<string> LoadScriptFiles(string scriptsDirectory)
    {
        if (!Directory.Exists(scriptsDirectory))
            throw new DirectoryNotFoundException($"Scripts directory does not exist: {scriptsDirectory}");

        Console.WriteLine($"Loading .sql files from: {scriptsDirectory}");

        var orderedFiles = new List<string>();

        static IEnumerable<string> GetFilesIfExists(string root, string subfolder)
        {
            var path = Path.Combine(root, subfolder);
            if (!Directory.Exists(path))
                return Enumerable.Empty<string>();

            return Directory
                .GetFiles(path, "*.sql", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        }

        orderedFiles.AddRange(GetFilesIfExists(scriptsDirectory, "domains"));

        orderedFiles.AddRange(GetFilesIfExists(scriptsDirectory, "tables"));

        orderedFiles.AddRange(GetFilesIfExists(scriptsDirectory, "procedures"));

        // 4) jakiekolwiek inne pliki .sql w drzewie, które nie zostały jeszcze dodane
        var allFiles = Directory
            .GetFiles(scriptsDirectory, "*.sql", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        orderedFiles.AddRange(allFiles.Where(f => !orderedFiles.Contains(f)));

        Console.WriteLine($"Found {orderedFiles.Count} script file(s).");

        return orderedFiles;
    }

    /// <summary>
    /// Pobiera listę istniejących domen w bazie danych.
    /// </summary>
    private HashSet<string> LoadExistingDomains(FbConnection connection)
    {
        Console.WriteLine("Reading existing domains from database...");

        const string sql = @"
        SELECT TRIM(RDB$FIELD_NAME) AS FIELD_NAME
        FROM RDB$FIELDS
        WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0
          AND RDB$FIELD_NAME NOT LIKE 'RDB$%'";

        using var cmd = new FbCommand(sql, connection);
        using var reader = cmd.ExecuteReader();

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            set.Add(reader.GetString(reader.GetOrdinal("FIELD_NAME")));
        }

        Console.WriteLine($"Found {set.Count} existing domain(s).");
        return set;
    }

    /// <summary>
    /// Pobiera listę istniejących tabel w bazie danych.
    /// </summary>
    private HashSet<string> LoadExistingTables(FbConnection connection)
    {
        Console.WriteLine("Reading existing tables from database...");

        const string sql = @"
        SELECT TRIM(RDB$RELATION_NAME) AS RELATION_NAME
        FROM RDB$RELATIONS
        WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0
          AND RDB$VIEW_BLR IS NULL";

        using var cmd = new FbCommand(sql, connection);
        using var reader = cmd.ExecuteReader();

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            set.Add(reader.GetString(reader.GetOrdinal("RELATION_NAME")));
        }

        Console.WriteLine($"Found {set.Count} existing table(s).");
        return set;
    }

    /// <summary>
    /// Sprawdza, czy dany plik SQL należy do kategorii domen.
    /// </summary>
    private static bool IsSourcedFromDomainsFolder(string scriptPath)
    {
        var dir = Path.GetDirectoryName(scriptPath);
        return dir != null &&
               Path.GetFileName(dir)
                   .Equals("domains", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sprawdza, czy dany plik SQL należy do kategorii tabel.
    /// </summary>
    private static bool IsSourcedFromTablesFolder(string scriptPath)
    {
        var dir = Path.GetDirectoryName(scriptPath);
        return dir != null &&
               Path.GetFileName(dir)
                   .Equals("tables", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Wykonuje listę skryptów SQL w podanej bazie.
    /// </summary>
    internal void ExecuteScriptFiles(FbConnection context, List<string> scripts)
    {
        foreach (var scriptFile in scripts)
        {
            ExecuteScriptFile(context, scriptFile);
        }
    }

    /// <summary>
    /// Wykonuje jeden plik SQL w bazie danych, obsługując błędy i logując wynik.
    /// </summary>
    private void ExecuteScriptFile(FbConnection context, string scriptFile)
    {
        try
        {
            Console.WriteLine($"Executing script: {Path.GetFileName(scriptFile)}");

            var scriptContent = File.ReadAllText(scriptFile);

            if (string.IsNullOrWhiteSpace(scriptContent))
            {
                Console.WriteLine("Skipped (empty file)");
                return;
            }

            var script = new FbScript(scriptContent);
            script.Parse();

            var batch = new FbBatchExecution(context);
            batch.AppendSqlStatements(script);
            batch.Execute();

            Console.WriteLine($"Successfully executed: {Path.GetFileName(scriptFile)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in file {Path.GetFileName(scriptFile)}: {ex.Message}");
            throw;
        }
    }
}
