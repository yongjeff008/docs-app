namespace DocumentsApp.Api.Services;

/// <summary>
/// 本地磁盘文件存储服务
/// 所有路径均为相对 storage 根目录的相对路径（如 files/xxxx.docx）
/// </summary>
public interface IFileStorageService
{
    /// <summary>storage 根目录的绝对路径</summary>
    string RootPath { get; }

    /// <summary>预览缓存目录的绝对路径</summary>
    string PreviewsDirectory { get; }

    /// <summary>
    /// 保存上传的文件（GUID 命名），返回相对路径
    /// </summary>
    Task<string> SaveAsync(Stream content, string originalFileName, CancellationToken ct = default);

    /// <summary>相对路径 → 绝对路径（带越界校验）</summary>
    string GetFullPath(string relativePath);

    /// <summary>绝对路径 → 相对路径</summary>
    string GetRelativePath(string fullPath);

    bool Exists(string relativePath);

    /// <summary>删除文件（不存在时静默忽略）</summary>
    void Delete(string? relativePath);
}
