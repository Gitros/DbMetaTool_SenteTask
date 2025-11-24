using DbMetaTool.Application.Contracts;
using DbMetaTool.Domain.Models;
using FirebirdSql.Data.FirebirdClient;

namespace DbMetaTool.Application.Services;

/// <summary>
/// Odpowiada za odczytywanie metadanych z bazy Firebird:
/// domen, tabel (wraz z kolumnami) oraz procedur składowanych.
/// Klasa opiera się na systemowych tabelach RDB$*.
/// </summary>
internal sealed class DatabaseMetadataReader : IDatabaseMetadataReader
{
    /// <summary>
    /// Pobiera z bazy danych wszystkie domeny użytkownika (nie systemowe).
    /// Zwraca typy wraz z informacjami o długości, precyzji, skali,
    /// wartości domyślnej i nullable.
    /// </summary>
    public IReadOnlyList<DomainType> GetDomains(FbConnection connection)
    {
        const string sql = @"
            SELECT
                TRIM(RDB$FIELD_NAME)      AS FIELD_NAME,
                RDB$FIELD_TYPE            AS FIELD_TYPE,
                RDB$FIELD_SUB_TYPE        AS FIELD_SUB_TYPE,
                RDB$FIELD_LENGTH          AS FIELD_LENGTH,
                RDB$FIELD_PRECISION       AS FIELD_PRECISION,
                RDB$FIELD_SCALE           AS FIELD_SCALE,
                RDB$CHARACTER_LENGTH      AS FIELD_CHAR_LENGTH,
                RDB$NULL_FLAG             AS NULL_FLAG,
                RDB$DEFAULT_SOURCE        AS DEFAULT_SOURCE
            FROM RDB$FIELDS
            WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0
              AND RDB$FIELD_NAME NOT LIKE 'RDB$%'";

        using var cmd = new FbCommand(sql, connection);
        using var reader = cmd.ExecuteReader();

        var result = new List<DomainType>();

        while (reader.Read())
        {
            var fieldType = GetInt16OrNull(reader, "FIELD_TYPE") ?? 0;
            var subType = GetInt16OrNull(reader, "FIELD_SUB_TYPE");
            var length = GetInt32OrNull(reader, "FIELD_LENGTH");
            var precision = GetInt16OrNull(reader, "FIELD_PRECISION");
            var scale = GetInt16OrNull(reader, "FIELD_SCALE");
            var charLen = GetInt32OrNull(reader, "FIELD_CHAR_LENGTH");

            var sqlType = MapFirebirdTypeToSql(fieldType, subType, length, precision, scale, charLen);

            var def = new DomainType
            {
                Name = reader.GetString(reader.GetOrdinal("FIELD_NAME")),
                DataType = sqlType,
                Length = charLen ?? length,
                Precision = precision,
                Scale = scale,
                IsNullable = IsNullable(reader, "NULL_FLAG"),
                DefaultSource = GetStringOrNull(reader, "DEFAULT_SOURCE")
            };

            result.Add(def);
        }

        return result;
    }

    /// <summary>
    /// Pobiera listę tabel użytkownika (bez widoków i obiektów systemowych),
    /// a następnie dla każdej z nich dołącza pełną listę kolumn.
    /// </summary>
    public IReadOnlyList<Table> GetTablesWithColumns(FbConnection connection)
    {
        const string tablesSql = @"
            SELECT TRIM(RDB$RELATION_NAME) AS RELATION_NAME
            FROM RDB$RELATIONS
            WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0
              AND RDB$VIEW_BLR IS NULL";

        using var tablesCmd = new FbCommand(tablesSql, connection);
        using var tablesReader = tablesCmd.ExecuteReader();

        var tables = new List<Table>();

        while (tablesReader.Read())
        {
            var tableName = tablesReader.GetString(tablesReader.GetOrdinal("RELATION_NAME"));

            var table = new Table
            {
                Name = tableName
            };

            table.Columns.AddRange(GetColumnsForTable(connection, tableName));

            tables.Add(table);
        }

        return tables;
    }

    /// <summary>
    /// Pobiera z bazy wszystkie procedury użytkownika,
    /// wraz z ich kodem źródłowym i parametrami wejścia/wyjścia.
    /// </summary>
    public IReadOnlyList<Procedure> GetProcedures(FbConnection connection)
    {
        const string sql = @"
        SELECT
            TRIM(RDB$PROCEDURE_NAME) AS PROCEDURE_NAME,
            RDB$PROCEDURE_SOURCE     AS PROCEDURE_SOURCE
        FROM RDB$PROCEDURES
        WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0
        ORDER BY RDB$PROCEDURE_NAME";

        var parameters = LoadProcedureParameters(connection);

        using var cmd = new FbCommand(sql, connection);
        using var reader = cmd.ExecuteReader();

        var result = new List<Procedure>();

        while (reader.Read())
        {
            var name = reader.GetString(reader.GetOrdinal("PROCEDURE_NAME"));

            parameters.TryGetValue(name, out var tuple);

            var proc = new Procedure
            {
                Name = name,
                Source = GetStringOrNull(reader, "PROCEDURE_SOURCE"),
                InputParameters = tuple.Inputs ?? new List<ProcedureParameter>(),
                OutputParameters = tuple.Outputs ?? new List<ProcedureParameter>()
            };

            result.Add(proc);
        }

        return result;
    }

    /// <summary>
    /// Pobiera wszystkie kolumny danej tabeli, mapując typy Firebird
    /// na odpowiadające im typy SQL (VARCHAR, DECIMAL, INTEGER, itp.).
    /// </summary>
    private IEnumerable<Column> GetColumnsForTable(FbConnection connection, string tableName)
    {
        const string columnsSql = @"
            SELECT
                TRIM(rf.RDB$FIELD_NAME)      AS FIELD_NAME,
                TRIM(rf.RDB$FIELD_SOURCE)    AS FIELD_SOURCE,
                f.RDB$FIELD_TYPE             AS FIELD_TYPE,
                f.RDB$FIELD_SUB_TYPE         AS FIELD_SUB_TYPE,
                f.RDB$FIELD_LENGTH           AS FIELD_LENGTH,
                f.RDB$FIELD_PRECISION        AS FIELD_PRECISION,
                f.RDB$FIELD_SCALE            AS FIELD_SCALE,
                f.RDB$CHARACTER_LENGTH       AS FIELD_CHAR_LENGTH,
                rf.RDB$NULL_FLAG             AS NULL_FLAG,
                COALESCE(rf.RDB$DEFAULT_SOURCE, f.RDB$DEFAULT_SOURCE) AS DEFAULT_SOURCE
            FROM RDB$RELATION_FIELDS rf
            JOIN RDB$FIELDS f
              ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE
            WHERE TRIM(rf.RDB$RELATION_NAME) = @TableName
            ORDER BY rf.RDB$FIELD_POSITION";

        using var cmd = new FbCommand(columnsSql, connection);
        cmd.Parameters.AddWithValue("@TableName", tableName);

        using var reader = cmd.ExecuteReader();
        var columns = new List<Column>();

        while (reader.Read())
        {
            var fieldType = GetInt16OrNull(reader, "FIELD_TYPE") ?? 0;
            var subType = GetInt16OrNull(reader, "FIELD_SUB_TYPE");
            var length = GetInt32OrNull(reader, "FIELD_LENGTH");
            var precision = GetInt16OrNull(reader, "FIELD_PRECISION");
            var scale = GetInt16OrNull(reader, "FIELD_SCALE");
            var charLen = GetInt32OrNull(reader, "FIELD_CHAR_LENGTH");

            var sqlType = MapFirebirdTypeToSql(fieldType, subType, length, precision, scale, charLen);

            var col = new Column
            {
                Name = reader.GetString(reader.GetOrdinal("FIELD_NAME")),
                FieldSource = reader.GetString(reader.GetOrdinal("FIELD_SOURCE")),
                DataType = sqlType,
                Length = charLen ?? length,
                Precision = precision,
                Scale = scale,
                IsNullable = IsNullable(reader, "NULL_FLAG"),
                DefaultSource = GetStringOrNull(reader, "DEFAULT_SOURCE")
            };

            columns.Add(col);
        }

        return columns;
    }

    /// <summary>
    /// Pobiera parametry wszystkich procedur z tabeli RDB$PROCEDURE_PARAMETERS,
    /// oddzielając parametry wejściowe od wyjściowych.
    /// </summary>
    private Dictionary<string, (List<ProcedureParameter> Inputs, List<ProcedureParameter> Outputs)>
    LoadProcedureParameters(FbConnection connection)
    {
        const string sql = @"
        SELECT
            TRIM(p.RDB$PROCEDURE_NAME) AS PROCEDURE_NAME,
            TRIM(p.RDB$PARAMETER_NAME) AS PARAMETER_NAME,
            p.RDB$PARAMETER_TYPE       AS PARAMETER_TYPE,     -- 0 = IN, 1 = OUT
            f.RDB$FIELD_TYPE           AS FIELD_TYPE,
            f.RDB$FIELD_SUB_TYPE       AS FIELD_SUB_TYPE,
            f.RDB$FIELD_LENGTH         AS FIELD_LENGTH,
            f.RDB$FIELD_PRECISION      AS FIELD_PRECISION,
            f.RDB$FIELD_SCALE          AS FIELD_SCALE,
            f.RDB$CHARACTER_LENGTH     AS FIELD_CHAR_LENGTH
        FROM RDB$PROCEDURE_PARAMETERS p
        JOIN RDB$FIELDS f
          ON f.RDB$FIELD_NAME = p.RDB$FIELD_SOURCE
        ORDER BY p.RDB$PROCEDURE_NAME, p.RDB$PARAMETER_TYPE, p.RDB$PARAMETER_NUMBER";

        using var cmd = new FbCommand(sql, connection);
        using var reader = cmd.ExecuteReader();

        var dict = new Dictionary<string, (List<ProcedureParameter>, List<ProcedureParameter>)>();

        while (reader.Read())
        {
            var procName = reader.GetString(reader.GetOrdinal("PROCEDURE_NAME"));
            var paramName = reader.GetString(reader.GetOrdinal("PARAMETER_NAME"));

            var fieldType = GetInt16OrNull(reader, "FIELD_TYPE") ?? 0;
            var subType = GetInt16OrNull(reader, "FIELD_SUB_TYPE");
            var length = GetInt32OrNull(reader, "FIELD_LENGTH");
            var precision = GetInt16OrNull(reader, "FIELD_PRECISION");
            var scale = GetInt16OrNull(reader, "FIELD_SCALE");
            var charLen = GetInt32OrNull(reader, "FIELD_CHAR_LENGTH");

            var sqlType = MapFirebirdTypeToSql(fieldType, subType, length, precision, scale, charLen);

            var param = new ProcedureParameter
            {
                Name = paramName,
                DataType = sqlType
            };

            var paramType = reader.GetInt16(reader.GetOrdinal("PARAMETER_TYPE"));

            if (!dict.TryGetValue(procName, out var tuple))
            {
                tuple = (new List<ProcedureParameter>(), new List<ProcedureParameter>());
                dict[procName] = tuple;
            }

            if (paramType == 0)
                tuple.Item1.Add(param);
            else
                tuple.Item2.Add(param);
        }

        return dict;
    }

    private static bool IsNullable(FbDataReader reader, string columnName)
    {
        var idx = reader.GetOrdinal(columnName);

        if (reader.IsDBNull(idx)) return true;

        var flag = reader.GetInt16(idx);

        return flag == 0;
    }

    private static string? GetStringOrNull(FbDataReader reader, string columnName)
    {
        var idx = reader.GetOrdinal(columnName);
        return reader.IsDBNull(idx) ? null : reader.GetString(idx);
    }

    private static short? GetInt16OrNull(FbDataReader reader, string columnName)
    {
        var idx = reader.GetOrdinal(columnName);
        return reader.IsDBNull(idx) ? null : reader.GetInt16(idx);
    }

    private static int? GetInt32OrNull(FbDataReader reader, string columnName)
    {
        var idx = reader.GetOrdinal(columnName);
        return reader.IsDBNull(idx) ? null : reader.GetInt32(idx);
    }

    /// <summary>
    /// Mapuje systemowy typ Firebird (FIELD_TYPE + SUB_TYPE)
    /// na odpowiadający mu SQL (VARCHAR, DECIMAL, TIMESTAMP itd.).
    /// </summary>
    private static string MapFirebirdTypeToSql(
        int fieldType,
        short? subType,
        int? fieldLength,
        short? precision,
        short? scale,
        int? charLength)
    {
        switch (fieldType)
        {
            case 7:
                return "SMALLINT";

            case 8:
                return "INTEGER";

            case 10:
                return "FLOAT";

            case 12:
                return "DATE";

            case 13:
                return "TIME";

            case 27:
                return "DOUBLE PRECISION";

            case 35:
                return "TIMESTAMP";

            case 14:
                {
                    var len = charLength ?? fieldLength ?? 1;
                    return $"CHAR({len})";
                }

            case 37:
                {
                    var len = charLength ?? fieldLength ?? 1;
                    return $"VARCHAR({len})";
                }

            case 16:
                {
                    var p = precision ?? 18;
                    var s = scale.HasValue ? Math.Abs(scale.Value) : 0;

                    if (subType is 1)
                        return $"NUMERIC({p},{s})";
                    if (subType is 2)
                        return $"DECIMAL({p},{s})";

                    return "BIGINT";
                }

            case 261:
                return "BLOB";

            default:
                return "UNKNOWN";
        }
    }
}
