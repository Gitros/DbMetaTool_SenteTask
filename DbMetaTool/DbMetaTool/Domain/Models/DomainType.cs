namespace DbMetaTool.Domain.Models;

public class DomainType
{
    public string Name { get; set; } = default!;
    public string DataType { get; set; } = default!;
    public int? Length { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public bool IsNullable { get; set; }
    public string? DefaultSource { get; set; }
}
