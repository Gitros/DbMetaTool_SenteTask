namespace DbMetaTool.Domain.Models;

public class Table
{
    public string Name { get; set; } = string.Empty;
    public List<Column> Columns { get; set; } = new();
}
