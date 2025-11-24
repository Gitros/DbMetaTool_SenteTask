using System;
using System.Collections.Generic;
using System.IO;
using DbMetaTool;
using FirebirdSql.Data.FirebirdClient;
using FluentAssertions;
using Xunit;

namespace DbMetaTool.Tests;

[CollectionDefinition("Database tests", DisableParallelization = true)]
public class DatabaseTestsCollection { }

[Collection("Database tests")]
public class ExportScriptsTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _dbDir;
    private readonly string _scriptsDir;
    private readonly string _exportDir;

    public ExportScriptsTests()
    {
        // bin/Debug/net8.0
        var baseDir = AppContext.BaseDirectory;

        _scriptsDir = Path.Combine(baseDir, "TestScripts");
        _testRoot = Path.Combine(baseDir, "TempTests", $"Export_{Guid.NewGuid():N}");
        _dbDir = Path.Combine(_testRoot, "Db1");
        _exportDir = Path.Combine(_testRoot, "Exported");

        Directory.CreateDirectory(_dbDir);
    }

    [Fact(DisplayName = "ExportScripts generuje pliki .sql dla domen, tabel i procedur")]
    public void ExportScripts_GeneratesSqlFilesPerObject()
    {
        // ARRANGE – najpierw zbuduj bazę z TestScripts
        Program.BuildDatabase(_dbDir, _scriptsDir);

        var dbPath = Path.Combine(_dbDir, "database.fdb");
        var connectionString = BuildConnectionString(dbPath);

        // ACT – eksport metadanych
        Program.ExportScripts(connectionString, _exportDir);

        // ASSERT – istnieją katalogi domen/tabel/procedur
        var domainsDir = Path.Combine(_exportDir, "domains");
        var tablesDir = Path.Combine(_exportDir, "tables");
        var proceduresDir = Path.Combine(_exportDir, "procedures");

        Directory.Exists(domainsDir).Should().BeTrue();
        Directory.Exists(tablesDir).Should().BeTrue();
        Directory.Exists(proceduresDir).Should().BeTrue();

        File.Exists(Path.Combine(domainsDir, "D_ID.sql")).Should().BeTrue();
        File.Exists(Path.Combine(domainsDir, "D_NAME.sql")).Should().BeTrue();
        File.Exists(Path.Combine(domainsDir, "D_DESCRIPTION.sql")).Should().BeTrue();
        File.Exists(Path.Combine(domainsDir, "D_CREATED_AT.sql")).Should().BeTrue();
        File.Exists(Path.Combine(domainsDir, "D_IS_ACTIVE.sql")).Should().BeTrue();

        File.Exists(Path.Combine(tablesDir, "CATEGORY.sql")).Should().BeTrue();
        File.Exists(Path.Combine(tablesDir, "TAG.sql")).Should().BeTrue();
        File.Exists(Path.Combine(tablesDir, "ITEM.sql")).Should().BeTrue();
        File.Exists(Path.Combine(tablesDir, "ITEM_TAG.sql")).Should().BeTrue();

        File.Exists(Path.Combine(proceduresDir, "P_GET_ACTIVE_ITEMS.sql")).Should().BeTrue();

        // --- dodatkowe lekkie sprawdzenie zawartości ---

        var dIdContent = File.ReadAllText(Path.Combine(domainsDir, "D_ID.sql"));
        dIdContent.Should().Contain("CREATE DOMAIN D_ID");
        dIdContent.Should().Contain("INTEGER");

        var categoryContent = File.ReadAllText(Path.Combine(tablesDir, "CATEGORY.sql"));
        categoryContent.Should().Contain("CREATE TABLE CATEGORY");
        categoryContent.Should().Contain("CATEGORY_ID");
        categoryContent.Should().Contain("NAME");

        var procContent = File.ReadAllText(Path.Combine(proceduresDir, "P_GET_ACTIVE_ITEMS.sql"));
        procContent.Should().Contain("CREATE OR ALTER PROCEDURE P_GET_ACTIVE_ITEMS");
        procContent.Should().Contain("RETURNS");
        procContent.Should().Contain("IS_ACTIVE = 1");
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

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
                Directory.Delete(_testRoot, recursive: true);
        }
        catch
        {
            // ignorujemy problemy z czyszczeniem
        }
    }
}
