using DbMetaTool.Application.Contracts;
using DbMetaTool.Domain.Models;
using FirebirdSql.Data.FirebirdClient;
using System.Text;

namespace DbMetaTool.Application.Services;

/// <summary>
/// Eksportuje strukturę bazy danych do plików skryptów.
/// Implementacja korzysta z IDatabaseMetadataReader, aby pobrać metadane,
/// a następnie zapisuje je w formacie SQL (domeny, tabele, procedury).
/// </summary>
internal class ScriptExporter : IScriptExporter
{
    private readonly IDatabaseMetadataReader _metadataReader;
    private readonly IFileManager _fileManager;
    

    /// <summary>
    /// Tworzy instancję eksportera skryptów.
    /// </summary>
    /// <param name="metadataReader">Komponent odpowiedzialny za odczyt metadanych z bazy.</param>
    /// <param name="fileSaver">Komponent obsługujący zapis plików na dysk.</param>
    public ScriptExporter(
        IDatabaseMetadataReader metadataReader,
        IFileManager fileSaver)
    {
        _metadataReader = metadataReader;
        _fileManager = fileSaver;
    }

    /// <summary>
    /// Generuje skrypty struktury bazy danych i zapisuje je do wskazanego katalogu.
    /// </summary>
    /// <param name="connectionString">Connection string do istniejącej bazy Firebird.</param>
    /// <param name="outputDirectory">Katalog wyjściowy dla wygenerowanych plików.</param>
    /// <param name="format">Format eksportu (na razie wspierany tylko SQL).</param>
    /// <exception cref="ArgumentException">Rzucane, gdy parametry są nieprawidłowe.</exception>
    public void GenerateDatabaseScripts(string connectionString, string outputDirectory, ScriptFormat format)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be empty.", nameof(connectionString));

        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory cannot be empty.", nameof(outputDirectory));

        Directory.CreateDirectory(outputDirectory);

        using var connection = new FbConnection(connectionString);
        connection.Open();

        var domains = _metadataReader.GetDomains(connection);
        var tables = _metadataReader.GetTablesWithColumns(connection);
        var procedures = _metadataReader.GetProcedures(connection);

        switch (format)
        {
            case ScriptFormat.Sql:
                ExportAsSql(outputDirectory, domains, tables, procedures);
                break;

            case ScriptFormat.Json:
                throw new NotImplementedException("JSON export is not implemented yet.");

            case ScriptFormat.Txt:
                throw new NotImplementedException("TXT export is not implemented yet.");

            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported script format.");
        }
    }

    /// <summary>
    /// Eksportuje wszystkie metadane do osobnych katalogów:
    /// /domains, /tables oraz /procedures.
    /// </summary>
    private void ExportAsSql(
        string outputDirectory,
        IReadOnlyList<DomainType> domains,
        IReadOnlyList<Table> tables,
        IReadOnlyList<Procedure> procedures)
    {
        var domainsDir = Path.Combine(outputDirectory, "domains");
        var tablesDir = Path.Combine(outputDirectory, "tables");
        var proceduresDir = Path.Combine(outputDirectory, "procedures");

        ExportDomainsAsSql(domainsDir, domains);
        ExportTablesAsSql(tablesDir, tables);
        ExportProceduresAsSql(proceduresDir, procedures);
    }

    /// <summary>
    /// Eksportuje wszystkie domeny do pojedynczych plików SQL.
    /// Każdy plik zawiera polecenie CREATE DOMAIN.
    /// </summary>
    private void ExportDomainsAsSql(string dir, IReadOnlyList<DomainType> domains)
    {
        if (domains.Count == 0)
            return;

        Directory.CreateDirectory(dir);

        foreach (var domain in domains)
        {
            var sb = new StringBuilder();

            sb.Append("CREATE DOMAIN ");
            sb.Append(domain.Name);
            sb.Append(" AS ");
            sb.Append(domain.DataType);

            if (!string.IsNullOrWhiteSpace(domain.DefaultSource))
            {
                sb.Append(' ');
                sb.Append(domain.DefaultSource.Trim()); // np. "DEFAULT 1"
            }

            if (!domain.IsNullable)
            {
                sb.Append(" NOT NULL");
            }

            sb.AppendLine(";");
            sb.AppendLine();

            var filePath = Path.Combine(dir, $"{domain.Name}.sql");
            _fileManager.SaveFile(filePath, sb.ToString());
        }
    }

    /// <summary>
    /// Eksportuje tabele i ich kolumny do plików SQL.
    /// Każdy plik zawiera polecenie CREATE TABLE.
    /// </summary>
    private void ExportTablesAsSql(string dir, IReadOnlyList<Table> tables)
    {
        if (tables.Count == 0)
            return;

        Directory.CreateDirectory(dir);

        foreach (var table in tables)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"CREATE TABLE {table.Name} (");

            for (int i = 0; i < table.Columns.Count; i++)
            {
                var col = table.Columns[i];

                sb.Append("    ");
                sb.Append(col.Name);
                sb.Append(' ');
                sb.Append(col.DataType);

                if (!string.IsNullOrWhiteSpace(col.DefaultSource))
                {
                    sb.Append(' ');
                    sb.Append(col.DefaultSource.Trim());
                }

                if (!col.IsNullable)
                {
                    sb.Append(" NOT NULL");
                }

                if (i < table.Columns.Count - 1)
                    sb.Append(',');

                sb.AppendLine();
            }

            sb.AppendLine(");");
            sb.AppendLine();

            var filePath = Path.Combine(dir, $"{table.Name}.sql");
            _fileManager.SaveFile(filePath, sb.ToString());
        }
    }

    /// <summary>
    /// Eksportuje procedury składowane do plików SQL,
    /// generując CREATE OR ALTER PROCEDURE wraz z parametrami.
    /// </summary>
    private void ExportProceduresAsSql(string dir, IReadOnlyList<Procedure> procedures)
    {
        if (procedures.Count == 0)
            return;

        Directory.CreateDirectory(dir);

        foreach (var proc in procedures)
        {
            if (string.IsNullOrWhiteSpace(proc.Source))
                continue;

            var sb = new StringBuilder();

            string body = proc.Source.Trim();

            if (body.EndsWith("."))
                body = body[..^1];

            sb.AppendLine("SET TERM ^ ;");
            sb.Append("CREATE OR ALTER PROCEDURE ");
            sb.Append(proc.Name);

            if (proc.InputParameters.Any())
            {
                sb.AppendLine(" (");
                sb.Append("    ");
                sb.Append(string.Join(", ",
                    proc.InputParameters.Select(p => $"{p.Name} {p.DataType}")));
                sb.AppendLine();
                sb.AppendLine(")");
            }
            else
            {
                sb.AppendLine();
            }

            if (proc.OutputParameters.Any())
            {
                sb.AppendLine("RETURNS (");
                sb.Append("    ");
                sb.Append(string.Join(", ",
                    proc.OutputParameters.Select(p => $"{p.Name} {p.DataType}")));
                sb.AppendLine();
                sb.AppendLine(")");
            }

            sb.AppendLine("AS");
            sb.AppendLine(body + " ^");
            sb.AppendLine("SET TERM ; ^");

            var filePath = Path.Combine(dir, $"{proc.Name}.sql");
            _fileManager.SaveFile(filePath, sb.ToString());
        }
    }

}
