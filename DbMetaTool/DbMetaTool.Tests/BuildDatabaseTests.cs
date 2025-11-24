using FirebirdSql.Data.FirebirdClient;
using FluentAssertions;

namespace DbMetaTool.Tests;

public class BuildDatabaseTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _dbDir;
    private readonly string _scriptsDir;

    public BuildDatabaseTests()
    {
        // baseDir -> ...\DbMetaTool.Tests\bin\Debug\net8.0\
        var baseDir = AppContext.BaseDirectory;

        // Katalog z kopiami skryptów (TestScripts → Copy to output dir)
        _scriptsDir = Path.Combine(baseDir, "TestScripts");

        // Osobny katalog roboczy dla tego zestawu testów
        _testRoot = Path.Combine(baseDir, "TempTests", $"BuildDb_{Guid.NewGuid():N}");
        _dbDir = Path.Combine(_testRoot, "Db1");

        Directory.CreateDirectory(_dbDir);
    }

    [Fact(DisplayName = "BuildDatabase tworzy bazę i poprawne obiekty")]
    public void BuildDatabase_CreatesDatabaseAndObjects()
    {
        // ACT
        Program.BuildDatabase(_dbDir, _scriptsDir);

        // ASSERT
        var dbPath = Path.Combine(_dbDir, "database.fdb");
        File.Exists(dbPath).Should().BeTrue($"plik bazy powinien istnieć w {_dbDir}");

        var cs = BuildConnectionString(dbPath);

        using var connection = new FbConnection(cs);
        connection.Open();

        var domains = ReadColumn(connection,
            @"SELECT TRIM(RDB$FIELD_NAME)
              FROM RDB$FIELDS
              WHERE COALESCE(RDB$SYSTEM_FLAG,0)=0
              AND RDB$FIELD_NAME NOT LIKE 'RDB$%'");

        domains.Should().Contain(new[]
        {
            "D_ID", "D_NAME", "D_DESCRIPTION", "D_CREATED_AT", "D_IS_ACTIVE"
        });

        var tables = ReadColumn(connection,
            @"SELECT TRIM(RDB$RELATION_NAME)
              FROM RDB$RELATIONS
              WHERE COALESCE(RDB$SYSTEM_FLAG,0)=0
                AND RDB$VIEW_BLR IS NULL");

        tables.Should().Contain(new[]
        {
            "CATEGORY", "TAG", "ITEM", "ITEM_TAG"
        });

        var procedures = ReadColumn(connection,
            @"SELECT TRIM(RDB$PROCEDURE_NAME)
              FROM RDB$PROCEDURES
              WHERE COALESCE(RDB$SYSTEM_FLAG,0)=0");

        procedures.Should().Contain("P_GET_ACTIVE_ITEMS");
    }

    private static string BuildConnectionString(string dbPath)
    {
        var csb = new FbConnectionStringBuilder
        {
            Database = dbPath,
            DataSource = "localhost",
            Port = 3050,
            UserID = "SYSDBA",
            Password = "masterkey",
            ServerType = FbServerType.Default,
            Charset = "UTF8",
            Pooling = false
        };
        return csb.ToString();
    }

    private static List<string> ReadColumn(FbConnection conn, string sql)
    {
        using var cmd = new FbCommand(sql, conn);
        using var reader = cmd.ExecuteReader();

        var list = new List<string>();
        while (reader.Read())
        {
            list.Add(reader.GetString(0).Trim());
        }
        return list;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
                Directory.Delete(_testRoot, recursive: true);
        }
        catch
        {
            // ignorujemy błędy
        }
    }
}
