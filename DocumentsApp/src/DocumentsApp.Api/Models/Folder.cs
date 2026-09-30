namespace DocumentsApp.Api.Models;

/// <summary>
/// 文件夹（多级树，通过 ParentId 自关联）
/// </summary>
public class Folder
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>父文件夹 ID，null 表示根级</summary>
    public int? ParentId { get; set; }

    public Folder? Parent { get; set; }

    public List<Folder> Children { get; set; } = new();

    public int SortOrder { get; set; }

    /// <summary>是否继承上级文件夹的权限（false = 已断开继承，只有本文件夹自身的授权生效）</summary>
    public bool InheritPermissions { get; set; } = true;

    public int CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
