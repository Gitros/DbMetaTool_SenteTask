namespace DbMetaTool.Application.Contracts;

internal interface IScriptExporter
{
    void GenerateDatabaseScripts(string connectionString, string outputDirectory, ScriptFormat format);
}

internal enum ScriptFormat
{
    Sql,
    Json,
    Txt
}
