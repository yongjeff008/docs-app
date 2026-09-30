using DocumentsApp.Api.Data;
using DocumentsApp.Api.Models;
using DocumentsApp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocumentsApp.Api.Controllers;

/// <summary>
/// 文件夹接口（多级树）
/// </summary>
[ApiController]
[Route("api/folders")]
[Authorize]
public class FoldersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPermissionService _permissionService;
    private readonly IAuditLogger _audit;

    public FoldersController(AppDbContext db, IPermissionService permissionService, IAuditLogger audit)
    {
        _db = db;
        _permissionService = permissionService;
        _audit = audit;
    }

    public record CreateFolderRequest(string? Name, int? ParentId);
    public record RenameFolderRequest(string? Name);
    public record MoveFolderRequest(int? NewParentId);

    /// <summary>树节点输出结构</summary>
    public class FolderNode
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? ParentId { get; set; }
        public int SortOrder { get; set; }
        public int DocumentCount { get; set; }
        /// <summary>当前用户对该文件夹的有效权限（位标志，前端据此控制按钮）</summary>
        public PermissionActions Actions { get; set; }
        /// <summary>是否继承上级权限（false = 已断开继承）</summary>
        public bool InheritPermissions { get; set; }
        /// <summary>是否有单独设置的授权</summary>
        public bool HasOwnGrants { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<FolderNode> Children { get; set; } = new();
    }

    /// <summary>获取完整文件夹树（嵌套 JSON）</summary>
    [HttpGet]
    public async Task<IActionResult> GetTree()
    {
        var userId = User.GetUserId();
        var folders = await _db.Folders.AsNoTracking().ToListAsync();
        var docCounts = await _db.Documents
            .GroupBy(d => d.FolderId)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.Count);

        // 权限过滤：null 表示不过滤（管理员）
        var accessible = await _permissionService.GetAccessibleFolderIdsAsync(userId);
        if (accessible != null)
            folders = folders.Where(f => accessible.Contains(f.Id)).ToList();

        var withGrants = (await _db.FolderPermissions.Select(p => p.FolderId).Distinct().ToListAsync()).ToHashSet();

        var nodes = new Dictionary<int, FolderNode>();
        foreach (var f in folders)
        {
            nodes[f.Id] = new FolderNode
            {
                Id = f.Id,
                Name = f.Name,
                ParentId = f.ParentId,
                SortOrder = f.SortOrder,
                DocumentCount = docCounts.GetValueOrDefault(f.Id),
                Actions = await _permissionService.GetFolderActionsAsync(userId, f.Id),
                // 顶层文件夹没有上级，始终视为继承
                InheritPermissions = f.InheritPermissions || f.ParentId == null,
                HasOwnGrants = withGrants.Contains(f.Id),
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt
            };
        }

        var roots = new List<FolderNode>();
        foreach (var node in nodes.Values)
        {
            // 父节点不可见时，将其提升为根节点显示
            if (node.ParentId is int pid && nodes.TryGetValue(pid, out var parent))
                parent.Children.Add(node);
            else
                roots.Add(node);
        }

        SortRecursive(roots);
        return Ok(roots);
    }

    /// <summary>获取单个文件夹详情（含面包屑路径）</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var userId = User.GetUserId();
        if (!await _permissionService.CanAccessAsync(userId, ResourceTypes.Folder, id, PermissionActions.Read))
            return Forbidden();

        var folder = await _db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
        if (folder == null) return NotFound(new { message = "文件夹不存在" });

        // 构建从根到当前的路径（只包含有查看权限的上级，避免泄露无权访问的文件夹名称）
        var accessible = await _permissionService.GetAccessibleFolderIdsAsync(userId);
        var all = await _db.Folders.AsNoTracking()
            .Select(f => new { f.Id, f.Name, f.ParentId })
            .ToDictionaryAsync(f => f.Id);
        var path = new List<object>();
        int? cursor = folder.Id;
        var guard = 0;
        while (cursor is int cid && all.TryGetValue(cid, out var item) && guard++ < 1000
               && (accessible == null || accessible.Contains(cid)))
        {
            path.Insert(0, new { id = item.Id, name = item.Name });
            cursor = item.ParentId;
        }

        var childCount = await _db.Folders.CountAsync(f => f.ParentId == id);
        var documentCount = await _db.Documents.CountAsync(d => d.FolderId == id);

        return Ok(new
        {
            folder.Id,
            folder.Name,
            folder.ParentId,
            folder.SortOrder,
            folder.CreatedAt,
            folder.UpdatedAt,
            childCount,
            documentCount,
            actions = await _permissionService.GetFolderActionsAsync(userId, id),
            path
        });
    }

    /// <summary>新建文件夹</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFolderRequest request)
    {
        var userId = User.GetUserId();
        // 在父文件夹下创建需要父文件夹的写权限；根级以 0 表示（仅管理员）
        if (!await _permissionService.CanAccessAsync(userId, ResourceTypes.Folder, request.ParentId ?? 0, PermissionActions.Write))
            return Forbidden(request.ParentId == null ? "只有管理员可以在根目录新建文件夹" : "没有在该文件夹中新建子文件夹的权限");

        var (name, error) = ValidateName(request.Name);
        if (error != null) return BadRequest(new { message = error });

        if (request.ParentId is int parentId && !await _db.Folders.AnyAsync(f => f.Id == parentId))
            return NotFound(new { message = "父文件夹不存在" });

        if (await NameExistsAsync(request.ParentId, name!, excludeId: null))
            return Conflict(new { message = $"同一位置已存在名为“{name}”的文件夹" });

        var maxSort = await _db.Folders
            .Where(f => f.ParentId == request.ParentId)
            .Select(f => (int?)f.SortOrder)
            .MaxAsync() ?? 0;

        var now = DateTime.UtcNow;
        var folder = new Folder
        {
            Name = name!,
            ParentId = request.ParentId,
            SortOrder = maxSort + 1,
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Folders.Add(folder);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("新建文件夹", ResourceTypes.Folder, folder.Id, folder.Name);
        return CreatedAtAction(nameof(Get), new { id = folder.Id }, ToDto(folder));
    }

    /// <summary>重命名文件夹（需要管理权限）</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Rename(int id, [FromBody] RenameFolderRequest request)
    {
        var userId = User.GetUserId();
        if (!await _permissionService.CanAccessAsync(userId, ResourceTypes.Folder, id, PermissionActions.Manage))
            return Forbidden("没有重命名该文件夹的权限（需要管理权限）");

        var folder = await _db.Folders.FirstOrDefaultAsync(f => f.Id == id);
        if (folder == null) return NotFound(new { message = "文件夹不存在" });

        var (name, error) = ValidateName(request.Name);
        if (error != null) return BadRequest(new { message = error });

        if (await NameExistsAsync(folder.ParentId, name!, excludeId: id))
            return Conflict(new { message = $"同一位置已存在名为“{name}”的文件夹" });

        var oldName = folder.Name;
        folder.Name = name!;
        folder.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("重命名文件夹", ResourceTypes.Folder, id, folder.Name, $"原名称：{oldName}");
        return Ok(ToDto(folder));
    }

    /// <summary>移动文件夹到新的父文件夹（newParentId 为 null 表示移到根级）</summary>
    [HttpPut("{id:int}/move")]
    public async Task<IActionResult> Move(int id, [FromBody] MoveFolderRequest request)
    {
        var userId = User.GetUserId();
        if (!await _permissionService.CanAccessAsync(userId, ResourceTypes.Folder, id, PermissionActions.Manage))
            return Forbidden("没有移动该文件夹的权限（需要管理权限）");
        if (!await _permissionService.CanAccessAsync(userId, ResourceTypes.Folder, request.NewParentId ?? 0, PermissionActions.Write))
            return Forbidden(request.NewParentId == null ? "只有管理员可以将文件夹移动到根目录" : "没有目标文件夹的写入权限");

        var folder = await _db.Folders.FirstOrDefaultAsync(f => f.Id == id);
        if (folder == null) return NotFound(new { message = "文件夹不存在" });

        if (folder.ParentId == request.NewParentId)
            return Ok(ToDto(folder));

        if (request.NewParentId is int newParentId)
        {
            if (newParentId == id)
                return BadRequest(new { message = "不能将文件夹移动到自身" });

            var parents = await _db.Folders.AsNoTracking()
                .Select(f => new { f.Id, f.ParentId })
                .ToDictionaryAsync(f => f.Id, f => f.ParentId);

            if (!parents.ContainsKey(newParentId))
                return NotFound(new { message = "目标文件夹不存在" });

            // 防止循环：目标不能是自身的子孙
            int? cursor = newParentId;
            var guard = 0;
            while (cursor is int cid && guard++ < 10000)
            {
                if (cid == id)
                    return BadRequest(new { message = "不能将文件夹移动到它的子文件夹中" });
                cursor = parents.GetValueOrDefault(cid);
            }
        }

        if (await NameExistsAsync(request.NewParentId, folder.Name, excludeId: id))
            return Conflict(new { message = $"目标位置已存在名为“{folder.Name}”的文件夹" });

        var maxSort = await _db.Folders
            .Where(f => f.ParentId == request.NewParentId)
            .Select(f => (int?)f.SortOrder)
            .MaxAsync() ?? 0;

        var targetName = request.NewParentId is int tid
            ? await _db.Folders.Where(f => f.Id == tid).Select(f => f.Name).FirstOrDefaultAsync()
            : "根目录";

        folder.ParentId = request.NewParentId;
        folder.SortOrder = maxSort + 1;
        folder.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("移动文件夹", ResourceTypes.Folder, id, folder.Name, $"移动到：{targetName}");
        return Ok(ToDto(folder));
    }

    /// <summary>删除文件夹（需要管理权限；非空则拒绝，返回 409）</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = User.GetUserId();
        if (!await _permissionService.CanAccessAsync(userId, ResourceTypes.Folder, id, PermissionActions.Manage))
            return Forbidden("没有删除该文件夹的权限（需要管理权限）");

        var folder = await _db.Folders.FirstOrDefaultAsync(f => f.Id == id);
        if (folder == null) return NotFound(new { message = "文件夹不存在" });

        var childCount = await _db.Folders.CountAsync(f => f.ParentId == id);
        var docCount = await _db.Documents.CountAsync(d => d.FolderId == id);
        if (childCount > 0 || docCount > 0)
        {
            var parts = new List<string>();
            if (childCount > 0) parts.Add($"{childCount} 个子文件夹");
            if (docCount > 0) parts.Add($"{docCount} 个文档");
            return Conflict(new { message = $"文件夹“{folder.Name}”非空（包含 {string.Join("、", parts)}），请先清空后再删除" });
        }

        _db.Folders.Remove(folder);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("删除文件夹", ResourceTypes.Folder, id, folder.Name);
        return NoContent();
    }

    // ---------- 私有辅助 ----------

    private IActionResult Forbidden(string message = "没有权限执行此操作")
        => StatusCode(StatusCodes.Status403Forbidden, new { message });

    private static readonly char[] InvalidNameChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

    private static (string? Name, string? Error) ValidateName(string? raw)
    {
        var name = raw?.Trim();
        if (string.IsNullOrEmpty(name)) return (null, "文件夹名称不能为空");
        if (name.Length > 100) return (null, "文件夹名称不能超过 100 个字符");
        if (name.IndexOfAny(InvalidNameChars) >= 0) return (null, "文件夹名称不能包含 / \\ : * ? \" < > | 等字符");
        if (name is "." or "..") return (null, "文件夹名称无效");
        return (name, null);
    }

    private Task<bool> NameExistsAsync(int? parentId, string name, int? excludeId)
        => _db.Folders.AnyAsync(f => f.ParentId == parentId && f.Name == name && (excludeId == null || f.Id != excludeId));

    private static void SortRecursive(List<FolderNode> nodes)
    {
        nodes.Sort((a, b) => a.SortOrder != b.SortOrder
            ? a.SortOrder.CompareTo(b.SortOrder)
            : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
        foreach (var n in nodes) SortRecursive(n.Children);
    }

    private static object ToDto(Folder f) => new
    {
        f.Id,
        f.Name,
        f.ParentId,
        f.SortOrder,
        f.CreatedAt,
        f.UpdatedAt
    };
}
