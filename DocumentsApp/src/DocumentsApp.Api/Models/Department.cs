namespace DocumentsApp.Api.Models;

/// <summary>
/// 部门（用于按部门批量授权）
/// </summary>
public class Department
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
}
