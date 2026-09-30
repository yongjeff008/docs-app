namespace DocumentsApp.Api.Models;

/// <summary>
/// 文档（实际文件存放在磁盘上，数据库只记录元数据）
/// </summary>
public class Document
{
    public int Id { get; set; }

    public int FolderId { get; set; }

    public Folder Folder { get; set; } = null!;

    /// <summary>原始文件名（含扩展名）</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>相对 storage 根目录的存储路径（GUID 命名）</summary>
    public string StoredPath { get; set; } = string.Empty;

    /// <summary>文件大小（字节）</summary>
    public long Size { get; set; }

    public string MimeType { get; set; } = "application/octet-stream";

    /// <summary>缓存的预览 PDF 路径（相对 storage 根目录）</summary>
    public string? PreviewPath { get; set; }

    public DateTime? PreviewGeneratedAt { get; set; }

    /// <summary>是否继承所在文件夹的权限（false = 已断开继承，只有文档自身的授权生效）</summary>
    public bool InheritPermissions { get; set; } = true;

    public int CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
