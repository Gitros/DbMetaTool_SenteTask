using DbMetaTool.Domain.Models;
using FirebirdSql.Data.FirebirdClient;

namespace DbMetaTool.Application.Contracts;

internal interface IDatabaseMetadataReader
{
    IReadOnlyList<DomainType> GetDomains(FbConnection connection);
    IReadOnlyList<Table> GetTablesWithColumns(FbConnection connection);
    IReadOnlyList<Procedure> GetProcedures(FbConnection connection);
}