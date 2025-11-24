namespace DbMetaTool.Application.Contracts;

internal interface IDatabaseManager
{
    string CreateDatabase(string databaseDirectory);
    void ExecuteDatabaseScriptsWithDatabasePath(string databasePath, string scriptsDirectory);
    void ExecuteDatabaseScriptsWithConnectionString(string connectionString, string scriptsDirectory);
}
