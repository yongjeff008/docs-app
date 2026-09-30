using System.Diagnostics;
using DocumentsApp.Api.Models;

namespace DocumentsApp.Api.Services;

/// <summary>
/// 基于 LibreOffice headless 的预览实现
/// - PDF：直接返回原文件
/// - Office：soffice --headless --convert-to pdf --outdir &lt;目录&gt; &lt;源文件&gt;，结果缓存到 storage/previews/
/// - 图片 / 文本：直接返回
/// </summary>
public class LibreOfficePreviewService : IPreviewService
{
    private static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx" };

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".csv", ".json" };

    // LibreOffice 同一用户配置目录不支持并发实例，这里串行化转换
    private static readonly SemaphoreSlim ConvertLock = new(1, 1);

    private readonly IFileStorageService _storage;
    private readonly ILogger<LibreOfficePreviewService> _logger;
    private readonly string _sofficePath;
    private readonly int _timeoutSeconds;

    public LibreOfficePreviewService(
        IFileStorageService storage,
        IConfiguration configuration,
        ILogger<LibreOfficePreviewService> logger)
    {
        _storage = storage;
        _logger = logger;
        _sofficePath = configuration["Preview:LibreOfficePath"]
                       ?? "/Applications/LibreOffice.app/Contents/MacOS/soffice";
        _timeoutSeconds = configuration.GetValue("Preview:TimeoutSeconds", 30);
    }

    public PreviewKind GetKind(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase)) return PreviewKind.Pdf;
        if (OfficeExtensions.Contains(ext)) return PreviewKind.Office;
        if (ImageTypes.ContainsKey(ext)) return PreviewKind.Image;
        if (TextExtensions.Contains(ext)) return PreviewKind.Text;
        return PreviewKind.Unsupported;
    }

    public async Task<PreviewResult> GetPreviewAsync(Document document, CancellationToken ct = default)
    {
        // 以存储文件的扩展名为准（上传时确定，不受重命名影响）
        var ext = Path.GetExtension(document.StoredPath);
        var kind = GetKind(document.StoredPath);

        if (!_storage.Exists(document.StoredPath))
            return PreviewResult.Fail(StatusCodes.Status404NotFound, "源文件不存在或已被删除");

        var sourcePath = _storage.GetFullPath(document.StoredPath);

        switch (kind)
        {
            case PreviewKind.Pdf:
                return PreviewResult.Ok(sourcePath, "application/pdf");

            case PreviewKind.Image:
                return PreviewResult.Ok(sourcePath, ImageTypes[ext]);

            case PreviewKind.Text:
                return PreviewResult.Ok(sourcePath, "text/plain; charset=utf-8");

            case PreviewKind.Office:
                return await ConvertOfficeAsync(document, sourcePath, ct);

            default:
                return PreviewResult.Fail(StatusCodes.Status415UnsupportedMediaType,
                    $"暂不支持预览该类型的文件（{(string.IsNullOrEmpty(ext) ? "无扩展名" : ext)}），请下载后查看");
        }
    }

    public void ClearCache(Document document)
    {
        if (string.IsNullOrEmpty(document.PreviewPath)) return;
        _storage.Delete(document.PreviewPath);
        document.PreviewPath = null;
        document.PreviewGeneratedAt = null;
    }

    /// <summary>
    /// Office → PDF 转换（带缓存与超时保护）
    /// </summary>
    private async Task<PreviewResult> ConvertOfficeAsync(Document document, string sourcePath, CancellationToken ct)
    {
        // 命中缓存直接返回
        if (TryGetCached(document, out var cached))
            return PreviewResult.Ok(cached, "application/pdf");

        if (!File.Exists(_sofficePath))
            return PreviewResult.Fail(StatusCodes.Status503ServiceUnavailable,
                "服务器未安装 LibreOffice，无法预览 Office 文档，请下载后查看");

        await ConvertLock.WaitAsync(ct);
        try
        {
            // 等锁期间可能已被其他请求转换完成
            if (TryGetCached(document, out cached))
                return PreviewResult.Ok(cached, "application/pdf");

            var outDir = _storage.PreviewsDirectory;
            var outputPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(sourcePath) + ".pdf");
            if (File.Exists(outputPath)) File.Delete(outputPath);

            var psi = new ProcessStartInfo
            {
                FileName = _sofficePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--headless");
            psi.ArgumentList.Add("--convert-to");
            psi.ArgumentList.Add("pdf");
            psi.ArgumentList.Add("--outdir");
            psi.ArgumentList.Add(outDir);
            psi.ArgumentList.Add(sourcePath);

            using var process = new Process { StartInfo = psi };
            var sw = Stopwatch.StartNew();
            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动 LibreOffice 失败");
                return PreviewResult.Fail(StatusCodes.Status500InternalServerError, "启动 LibreOffice 失败，无法生成预览");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 超时或请求取消：结束整个进程树
                try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
                if (ct.IsCancellationRequested) throw;
                _logger.LogWarning("LibreOffice 转换超时（{Seconds}s）：{File}", _timeoutSeconds, document.FileName);
                return PreviewResult.Fail(StatusCodes.Status504GatewayTimeout,
                    $"文档转换超时（超过 {_timeoutSeconds} 秒），请稍后重试或下载后查看");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                _logger.LogError("LibreOffice 转换失败，退出码 {Code}，输出：{Out} {Err}", process.ExitCode, stdout, stderr);
                return PreviewResult.Fail(StatusCodes.Status500InternalServerError,
                    "文档转换失败，文件可能已损坏或格式不受支持，请下载后查看");
            }

            _logger.LogInformation("LibreOffice 转换完成：{File}，耗时 {Ms}ms", document.FileName, sw.ElapsedMilliseconds);

            document.PreviewPath = _storage.GetRelativePath(outputPath);
            document.PreviewGeneratedAt = DateTime.UtcNow;
            return PreviewResult.Ok(outputPath, "application/pdf");
        }
        finally
        {
            ConvertLock.Release();
        }
    }

    private bool TryGetCached(Document document, out string fullPath)
    {
        fullPath = string.Empty;
        // 源文件更新时由 ClearCache 清空 PreviewPath，这里只需判断缓存文件是否仍在
        if (string.IsNullOrEmpty(document.PreviewPath))
            return false;
        if (!_storage.Exists(document.PreviewPath))
            return false;
        fullPath = _storage.GetFullPath(document.PreviewPath);
        return true;
    }
}
