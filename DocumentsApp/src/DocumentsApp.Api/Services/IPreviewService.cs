using DocumentsApp.Api.Models;

namespace DocumentsApp.Api.Services;

/// <summary>
/// 预览类型
/// </summary>
public enum PreviewKind
{
    Pdf,
    Office,
    Image,
    Text,
    Unsupported
}

/// <summary>
/// 预览结果
/// </summary>
public class PreviewResult
{
    public bool Success { get; init; }

    /// <summary>待返回文件的绝对路径</summary>
    public string? FilePath { get; init; }

    public string? ContentType { get; init; }

    /// <summary>失败时的 HTTP 状态码</summary>
    public int StatusCode { get; init; } = 200;

    /// <summary>失败时的中文提示</summary>
    public string? ErrorMessage { get; init; }

    public static PreviewResult Ok(string path, string contentType)
        => new() { Success = true, FilePath = path, ContentType = contentType };

    public static PreviewResult Fail(int statusCode, string message)
        => new() { Success = false, StatusCode = statusCode, ErrorMessage = message };
}

/// <summary>
/// 文档预览服务
/// </summary>
public interface IPreviewService
{
    /// <summary>根据文件名判断预览类型</summary>
    PreviewKind GetKind(string fileName);

    /// <summary>
    /// 获取预览文件（Office 文档会转换为 PDF 并缓存，缓存信息写回 Document 实体，由调用方保存）
    /// </summary>
    Task<PreviewResult> GetPreviewAsync(Document document, CancellationToken ct = default);

    /// <summary>清除文档的预览缓存（源文件更新或删除时调用）</summary>
    void ClearCache(Document document);
}
