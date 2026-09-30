namespace DocumentsApp.Api.Services;

/// <summary>
/// 本地磁盘存储实现
/// 目录结构：
///   {RootPath}/files/     原始文件（GUID 命名）
///   {RootPath}/previews/  Office 转换后的 PDF 缓存
/// </summary>
public class FileStorageService : IFileStorageService
{
    private const string FilesFolder = "files";
    private const string PreviewsFolder = "previews";

    private readonly ILogger<FileStorageService> _logger;

    public string RootPath { get; }
    public string PreviewsDirectory { get; }

    public FileStorageService(IConfiguration configuration, ILogger<FileStorageService> logger)
    {
        _logger = logger;

        var configured = configuration["Storage:RootPath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = "~/DocumentsApp/storage";

        // 支持 ~ 开头的路径
        if (configured.StartsWith("~"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configured = Path.Combine(home, configured.TrimStart('~').TrimStart('/', '\\'));
        }

        RootPath = Path.GetFullPath(configured);
        PreviewsDirectory = Path.Combine(RootPath, PreviewsFolder);

        Directory.CreateDirectory(Path.Combine(RootPath, FilesFolder));
        Directory.CreateDirectory(PreviewsDirectory);
    }

    public async Task<string> SaveAsync(Stream content, string originalFileName, CancellationToken ct = default)
    {
        // 仅保留扩展名，文件名用 GUID，避免冲突与特殊字符问题
        var ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (ext.Length > 16 || ext.Any(c => !char.IsLetterOrDigit(c) && c != '.'))
            ext = string.Empty;

        var relative = Path.Combine(FilesFolder, $"{Guid.NewGuid():N}{ext}");
        var full = GetFullPath(relative);

        await using (var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await content.CopyToAsync(fs, ct);
        }

        return relative.Replace('\\', '/');
    }

    public string GetFullPath(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(RootPath, relativePath));
        // 防止路径穿越
        if (!full.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("非法的存储路径");
        return full;
    }

    public string GetRelativePath(string fullPath)
        => Path.GetRelativePath(RootPath, fullPath).Replace('\\', '/');

    public bool Exists(string relativePath) => File.Exists(GetFullPath(relativePath));

    public void Delete(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return;
        try
        {
            var full = GetFullPath(relativePath);
            if (File.Exists(full)) File.Delete(full);
        }
        catch (Exception ex)
        {
            // 删除失败不影响主流程，记录日志即可
            _logger.LogWarning(ex, "删除文件失败：{Path}", relativePath);
        }
    }
}
