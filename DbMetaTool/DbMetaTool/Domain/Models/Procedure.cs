namespace DbMetaTool.Domain.Models;

public class Procedure
{
    public string Name { get; set; } = default!;
    public string? Source { get; set; }
    public List<ProcedureParameter> InputParameters { get; set; } = new();
    public List<ProcedureParameter> OutputParameters { get; set; } = new();
}
