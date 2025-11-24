using System;
using System.IO;
using System.Linq;
using DbMetaTool;
using FluentAssertions;
using FirebirdSql.Data.FirebirdClient;
using Xunit;

namespace DbMetaTool.Tests;

public class UpdateDatabaseTests
{
    private readonly string _testRootDir;
    private readonly string _scriptsDir;
    private readonly string _updateScriptsDir;

    public UpdateDatabaseTests()
    {
        var baseDir = AppContext.BaseDirectory;
        var projectDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));

        _testRootDir = Path.Combine(projectDir, "Temp", $"UpdateDb_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRootDir);

        _scriptsDir = Path.Combine(baseDir, "TestScripts");
        _updateScriptsDir = Path.Combine(baseDir, "TestProceduresUpdate");
    }

    private static string BuildConnectionString(string dbPath)
    {
        var builder = new FbConnectionStringBuilder
        {
            Database = dbPath,
            DataSource = "localhost",
            Port = 3050,
            UserID = "SYSDBA",
            Password = "masterkey",
            Charset = "UTF8",
            Pooling = false
        };

        return builder.ToString();
    }

    /// <summary>
    /// Pomocnicza metoda – zwraca nazwy procedur użytkownika z bazy.
    /// </summary>
    private static string[] GetUserProcedures(string connectionString)
    {
        using var connection = new FbConnection(connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT TRIM(RDB$PROCEDURE_NAME)
            FROM RDB$PROCEDURES
            WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0
            ORDER BY RDB$PROCEDURE_NAME";

        using var reader = cmd.ExecuteReader();
        var list = new System.Collections.Generic.List<string>();

        while (reader.Read())
        {
            list.Add(reader.GetString(0));
        }

        return list.ToArray();
    }

    [Fact(DisplayName = "UpdateDatabase z tymi samymi skryptami jest idempotentny")]
    public void UpdateDatabase_WithSameScripts_IsIdempotent()
    {
        // ARRANGE
        var dbDir = Path.Combine(_testRootDir, "Db1");
        Directory.CreateDirectory(dbDir);

        Program.BuildDatabase(dbDir, _scriptsDir);

        var dbPath = Path.Combine(dbDir, "database.fdb");
        File.Exists(dbPath).Should().BeTrue("po BuildDatabase baza powinna istnieć");

        var connectionString = BuildConnectionString(dbPath);
        var proceduresBefore = GetUserProcedures(connectionString);

        // ACT
        Action act = () => Program.UpdateDatabase(connectionString, _scriptsDir);

        // ASSERT
        act.Should().NotThrow("UpdateDatabase z tym samym zestawem skryptów powinien być bezpieczny (idempotentny)");

        var proceduresAfter = GetUserProcedures(connectionString);

        foreach (var proc in proceduresBefore)
        {
            proceduresAfter.Should().Contain(proc);
        }

    }

    [Fact(DisplayName = "UpdateDatabase dodaje nowe procedury z katalogu update")]
    public void UpdateDatabase_AddsNewProceduresFromUpdateScripts()
    {
        // ARRANGE
        var dbDir = Path.Combine(_testRootDir, "Db2");
        Directory.CreateDirectory(dbDir);

        Program.BuildDatabase(dbDir, _scriptsDir);

        var dbPath = Path.Combine(dbDir, "database.fdb");
        File.Exists(dbPath).Should().BeTrue();

        var connectionString = BuildConnectionString(dbPath);

        var proceduresBefore = GetUserProcedures(connectionString);
        var newProcedureName = "LOG_ITEM_CHANGE";

        proceduresBefore.Should().NotContain(
            newProcedureName,
            $"procedura {newProcedureName} nie powinna istnieć przed aktualizacją");

        // ACT – update bazę skryptami z TestProceduresUpdate
        Action act = () => Program.UpdateDatabase(connectionString, _updateScriptsDir);

        // ASSERT
        act.Should().NotThrow("UpdateDatabase ze skryptami procedur nie powinien rzucać wyjątków");

        var proceduresAfter = GetUserProcedures(connectionString);
        proceduresAfter.Should().Contain(
            newProcedureName,
            $"procedura {newProcedureName} powinna zostać dodana przez UpdateDatabase");
    }
}
