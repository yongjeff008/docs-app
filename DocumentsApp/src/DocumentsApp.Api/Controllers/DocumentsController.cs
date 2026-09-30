using DocumentsApp.Api.Data;
using DocumentsApp.Api.Models;
using DocumentsApp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

namespace DocumentsApp.Api.Controllers;

/// <summary>
/// 文档接口：列表 / 上传 / 下载 / 预览 / 重命名 / 删除
/// </summary>
[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();
    private static readonly char[] InvalidNameChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

    private readonly AppDbContext _db;
    private readonly IPermissionService _permissionService;
    private readonly IFileStorageService _storage;
    private readonly IPreviewService _previewService;
    private readonly IAuditLogger _audit;
    private readonly ILogger<DocumentsController> _logger;
    private readonly long _maxFileSizeBytes;

    public DocumentsController(
        AppDbContext db,
        IPermissionService permissionService,
        IFileStorageService storage,
        IPreviewService previewService,
        IAuditLogger audit,
        IConfiguration configuration,
        ILogger<DocumentsController> logger)
    {
        _db = db;
        _permissionService = permissionService;
        _storage = storage;
        _previewService = previewService;
        _audit = audit;
        _logger = logger;
        _maxFileSizeBytes = configuration.GetValue("Storage:MaxFileSizeMB", 100) * 1024L * 1024L;
    }

    public record RenameDocumentRequest(string? FileName);

    /// <summary>
    /// 文档列表：?folderId= 查询指定文件夹；?q= 按文件名搜索（可与 folderId 组合，不带 folderId 时全局搜索）
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? folderId, [FromQuery] string? q)
    {
        var userId = User.GetUserId();
        var keyword = q?.Trim();

        if (folderId == null && string.IsNullOrEmpty(keyword))
            return BadRequest(new { message = "请指定文件夹" });

        var query = _db.Documents.AsNoTracking().AsQueryable();

        if (folderId is int fid)
        {
            if (!await _permissionService.CanAccessAsync(userId, ResourceTypes.Folder, fid, PermissionActions.Read))
                return Forbidden();
            if (!await _db.Folders.AnyAsync(f => f.Id == fid))
                return NotFound(new { message = "文件夹不存在" });
            query = query.Where(d => d.FolderId == fid);
        }
        else
        {
            // 全局搜索：可见文件夹中的文档 + 单独授权给自己的文档（null = 不过滤）；最终以逐个文档的权限为准
            var accessible = await _permissionService.GetAccessibleFolderIdsAsync(userId);
            var granted = await _permissionService.GetDocumentIdsGrantedDirectlyAsync(userId);
            if (accessible != null)
            {
                var folderIds = accessible.ToList();
                var docIds = (granted ?? new HashSet<int>()).ToList();
                query = query.Where(d => folderIds.Contains(d.FolderId) || docIds.Contains(d.Id));
            }
        }

        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(d => EF.Functions.Like(d.FileName, $"%{EscapeLike(keyword)}%", "\\"));

        var docs = await query
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new
            {
                d.Id,
                d.FolderId,
                FolderName = d.Folder.Name,
                d.FileName,
                d.StoredPath,
                d.Size,
                d.MimeType,
                d.CreatedBy,
                CreatorName = _db.Users.Where(u => u.Id == d.CreatedBy).Select(u => u.DisplayName).FirstOrDefault(),
                d.InheritPermissions,
                d.CreatedAt,
                d.UpdatedAt
            })
            .Take(1000)
            .ToListAsync();

        var docIdsInPage = docs.Select(d => d.Id).ToList();
        var withGrants = (await _db.DocumentPermissions
            .Where(p => docIdsInPage.Contains(p.DocumentId))
            .Select(p => p.DocumentId).Distinct().ToListAsync()).ToHashSet();

        // 逐个计算文档权限：断开继承且未授权给自己的文档不显示
        var result = new List<object>();
        foreach (var d in docs)
        {
            var actions = await _permissionService.GetDocumentActionsAsync(userId, d.Id, d.FolderId, d.InheritPermissions);
            if (!actions.HasFlag(PermissionActions.Read)) continue;
            result.Add(new
            {
                d.Id,
                d.FolderId,
                d.FolderName,
                d.FileName,
                d.Size,
                d.MimeType,
                d.CreatedBy,
                d.CreatorName,
                d.CreatedAt,
                d.UpdatedAt,
                Actions = actions,
                d.InheritPermissions,
                HasOwnGrants = withGrants.Contains(d.Id),
                PreviewKind = _previewService.GetKind(d.StoredPath).ToString().ToLowerInvariant()
            });
        }
        return Ok(result);
    }

    /// <summary>上传文档（multipart/form-data：file, folderId）</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(long.MaxValue)] // 实际大小限制由 Kestrel 全局配置与下方检查控制
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, [FromForm] int? folderId, CancellationToken ct)
    {
        var userId = User.GetUserId();

        if (folderId is not int fid)
            return BadRequest(new { message = "请选择要上传到的文件夹" });

        if (!await _permissionService.CanAccessAsync(userId, ResourceTypes.Folder, fid, PermissionActions.Write))
            return Forbidden("没有向该文件夹上传文档的权限");

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "请选择要上传的文件（不能为空文件）" });

        if (file.Length > _maxFileSizeBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { message = $"文件过大，单个文件不能超过 {_maxFileSizeBytes / 1024 / 1024} MB" });

        if (!await _db.Folders.AnyAsync(f => f.Id == fid, ct))
            return NotFound(new { message = "目标文件夹不存在" });

        var fileName = Path.GetFileName(file.FileName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(fileName)) fileName = "未命名文件";
        if (fileName.Length > 200)
        {
            var ext = Path.GetExtension(fileName);
            fileName = fileName[..(200 - ext.Length)] + ext;
        }

        string storedPath;
        await using (var stream = file.OpenReadStream())
        {
            storedPath = await _storage.SaveAsync(stream, fileName, ct);
        }

        var now = DateTime.UtcNow;
        var doc = new Document
        {
            FolderId = fid,
            FileName = fileName,
            StoredPath = storedPath,
            Size = file.Length,
            MimeType = GetMimeType(fileName),
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            _db.Documents.Add(doc);
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // 数据库写入失败时清理已落盘的文件
            _storage.Delete(storedPath);
            throw;
        }

        _logger.LogInformation("上传文档：{File}（{Size} 字节）到文件夹 {FolderId}", fileName, file.Length, fid);
        await _audit.LogAsync("上传文档", ResourceTypes.Document, doc.Id, fileName, await FolderDetailAsync(fid));
        return Ok(ToDto(doc));
    }

    /// <summary>下载原始文件</summary>
    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Download(int id)
    {
        var userId = User.GetUserId();
        if (!await _permissionService.CanAccessAsync(userId, ResourceTypes.Document, id, PermissionActions.Read))
            return Forbidden("没有下载该文档的权限");

        var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound(new { message = "文档不存在" });
        if (!_storage.Exists(doc.StoredPath)) return NotFound(new { message = "源文件不存在或已被删除" });

        // 断点续传的后续分段请求不重复记录
        var range = Request.Headers.Range.ToString();
        if (string.IsNullOrEmpty(range) || range.StartsWith("bytes=0-", StringComparison.Ordinal))
            await _audit.LogAsync("下载文档", ResourceTypes.Document, id, doc.FileName, await FolderDetailAsync(doc.FolderId));

        return PhysicalFile(_storage.GetFullPath(doc.StoredPath), doc.MimeType, doc.FileName, enableRangeProcessing: true);
    }

    /// <summary>预览（PDF/图片/文本直接返回；Office 转 PDF 后返回；其他 415）</summary>
    [HttpGet("{id:int}/preview")]
    public async Task<IActionResult> Preview(int id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (!await _permissionService.CanAccessAsync(userId, ResourceTypes.Document, id, PermissionActions.Read))
            return Forbidden("没有查看该文档的权限");

        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc == null) return NotFound(new { message = "文档不存在" });

        var previewPathBefore = doc.PreviewPath;
        var result = await _previewService.GetPreviewAsync(doc, ct);

        // 转换生成了新缓存，写回数据库
        if (doc.PreviewPath != previewPathBefore)
            await _db.SaveChangesAsync(CancellationToken.None);

        if (!result.Success)
            return StatusCode(result.StatusCode, new { message = result.ErrorMessage });

        return PhysicalFile(result.FilePath!, result.ContentType!, enableRangeProcessing: true);
    }

    /// <summary>重命名文档</summary>
    [HttpPut("{id:int}/rename")]
    public async Task<IActionResult> Rename(int id, [FromBody] RenameDocumentRequest request)
    {
        var userId = User.GetUserId();
        if (!await _permissionService.CanAccessAsync(userId, ResourceTypes.Document, id, PermissionActions.Write))
            return Forbidden("没有重命名该文档的权限");

        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound(new { message = "文档不存在" });

        var name = request.FileName?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { message = "文件名不能为空" });
        if (name.IndexOfAny(InvalidNameChars) >= 0)
            return BadRequest(new { message = "文件名不能包含 / \\ : * ? \" < > | 等字符" });

        // 未填写扩展名时自动保留原扩展名
        var oldExt = Path.GetExtension(doc.FileName);
        if (string.IsNullOrEmpty(Path.GetExtension(name)) && !string.IsNullOrEmpty(oldExt))
            name += oldExt;

        if (name.Length > 200) return BadRequest(new { message = "文件名不能超过 200 个字符" });

        var oldName = doc.FileName;
        doc.FileName = name;
        doc.MimeType = GetMimeType(name);
        doc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("重命名文档", ResourceTypes.Document, id, name, $"原名称：{oldName}");
        return Ok(ToDto(doc));
    }

    /// <summary>删除文档（同时删除原文件与预览缓存）</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = User.GetUserId();
        if (!await _permissionService.CanAccessAsync(userId, ResourceTypes.Document, id, PermissionActions.Delete))
            return Forbidden("没有删除该文档的权限");

        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound(new { message = "文档不存在" });

        var storedPath = doc.StoredPath;
        var folderDetail = await FolderDetailAsync(doc.FolderId);
        _previewService.ClearCache(doc);
        _db.Documents.Remove(doc);
        await _db.SaveChangesAsync();
        _storage.Delete(storedPath);

        await _audit.LogAsync("删除文档", ResourceTypes.Document, id, doc.FileName, folderDetail);
        return NoContent();
    }

    // ---------- 私有辅助 ----------

    private IActionResult Forbidden(string message = "没有权限执行此操作")
        => StatusCode(StatusCodes.Status403Forbidden, new { message });

    /// <summary>日志用：所在文件夹说明</summary>
    private async Task<string> FolderDetailAsync(int folderId)
        => "文件夹：" + (await _db.Folders.Where(f => f.Id == folderId).Select(f => f.Name).FirstOrDefaultAsync() ?? folderId.ToString());

    private static string GetMimeType(string fileName)
        => ContentTypes.TryGetContentType(fileName, out var type) ? type : "application/octet-stream";

    private static string EscapeLike(string s)
        => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private object ToDto(Document d) => new
    {
        d.Id,
        d.FolderId,
        d.FileName,
        d.Size,
        d.MimeType,
        d.CreatedBy,
        d.CreatedAt,
        d.UpdatedAt,
        d.InheritPermissions,
        PreviewKind = _previewService.GetKind(d.StoredPath).ToString().ToLowerInvariant()
    };
}
