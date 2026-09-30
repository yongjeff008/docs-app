using DocumentsApp.Api.Data;
using DocumentsApp.Api.Models;
using DocumentsApp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocumentsApp.Api.Controllers;

/// <summary>
/// 权限设置与明细（文件夹 / 文档通用）
///   GET  /api/permissions/{folder|document}/{id}          授权详情：继承状态、继承的授权、自身授权
///   PUT  /api/permissions/{folder|document}/{id}          保存：继承 / 断开 + 自身授权（整体替换）
///   GET  /api/permissions/{folder|document}/{id}/check    查询某成员的最终权限及来源
///   GET  /api/permissions/overview                        权限总览（仅管理员）
/// 以上除总览外均需要该资源的管理权限。
/// </summary>
[ApiController]
[Route("api/permissions")]
[Authorize]
public class PermissionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPermissionService _permissionService;
    private readonly IAuditLogger _audit;

    public PermissionsController(AppDbContext db, IPermissionService permissionService, IAuditLogger audit)
    {
        _db = db;
        _permissionService = permissionService;
        _audit = audit;
    }

    public record PermissionEntryRequest(string? SubjectType, string? SubjectId, PermissionActions Actions, bool? InheritToChildren);
    public record SavePermissionsRequest(bool? InheritPermissions, List<PermissionEntryRequest>? Entries);

    /// <summary>被操作的资源（文件夹或文档）</summary>
    private sealed record Target(string Type, int Id, string Name, int FolderId, int? ParentFolderId,
        bool InheritPermissions, string TypeName);

    // ---------- 详情 ----------

    [HttpGet("{type}/{id:int}")]
    public async Task<IActionResult> Get(string type, int id)
    {
        var (target, error) = await ResolveAsync(type, id);
        if (error != null) return error;

        var graph = await PermissionGraph.LoadAsync(_db);
        var t = target!;

        // inherited：上级传来的授权（断开状态下也返回“如果继承会得到什么”，供恢复预览与断开时复制）
        List<GrantInfo> inherited;
        List<GrantInfo> own;
        GrantInfo? owner = null;
        if (t.Type == ResourceTypes.Folder)
        {
            inherited = graph.InheritedForFolder(t.Id, ignoreBreak: true);
            var folderOwn = graph.OwnForFolder(t.Id);
            owner = folderOwn.FirstOrDefault(g => g.Source == GrantSource.Owner);
            own = folderOwn.Where(g => g.Source == GrantSource.Direct).ToList();
        }
        else
        {
            inherited = graph.InheritedForDocument(t.FolderId);
            own = await _db.DocumentPermissions.AsNoTracking()
                .Where(p => p.DocumentId == t.Id)
                .OrderBy(p => p.Id)
                .Select(p => new GrantInfo(p.SubjectType, p.SubjectId, p.Actions, false, GrantSource.Direct, p.Id, null))
                .ToListAsync();
        }

        var names = await LoadSubjectNamesAsync(inherited.Concat(own).Append(owner).OfType<GrantInfo>()
            .Select(g => (g.SubjectType, g.SubjectId)));
        var adminCount = await _db.Users.CountAsync(u => u.Role == Roles.Admin && u.IsEnabled);

        return Ok(new
        {
            resourceType = t.Type,
            resourceId = t.Id,
            name = t.Name,
            typeName = t.TypeName,
            path = graph.PathOf(t.FolderId),
            inheritPermissions = t.InheritPermissions,
            // 顶层文件夹没有上级，无所谓继承 / 断开
            canInherit = t.Type == ResourceTypes.Document || t.ParentFolderId != null,
            parentName = t.Type == ResourceTypes.Document
                ? graph.Folders.GetValueOrDefault(t.FolderId)?.Name
                : t.ParentFolderId is int pid ? graph.Folders.GetValueOrDefault(pid)?.Name : null,
            owner = owner == null ? null : new
            {
                id = int.Parse(owner.SubjectId),
                name = names.GetValueOrDefault((owner.SubjectType, owner.SubjectId), "（已删除）")
            },
            adminCount,
            inherited = inherited.Select(g => ToDto(g, names, graph)),
            entries = own.Select(g => ToDto(g, names, graph))
        });
    }

    // ---------- 保存 ----------

    [HttpPut("{type}/{id:int}")]
    public async Task<IActionResult> Save(string type, int id, [FromBody] SavePermissionsRequest request)
    {
        var (target, error) = await ResolveAsync(type, id);
        if (error != null) return error;
        var t = target!;
        var userId = User.GetUserId();

        var input = request.Entries ?? new List<PermissionEntryRequest>();
        if (input.Count > 500) return BadRequest(new { message = "授权条目过多" });

        // 校验并去重（同一主体以最后一条为准）
        var cleaned = new Dictionary<(string, string), (PermissionActions Actions, bool Inherit)>();
        foreach (var e in input)
        {
            if (!SubjectTypes.IsValid(e.SubjectType) || string.IsNullOrWhiteSpace(e.SubjectId))
                return BadRequest(new { message = "授权对象无效" });
            var actions = PermissionLevels.Normalize(e.Actions);
            if (actions == PermissionActions.None)
                return BadRequest(new { message = "请为每个授权对象选择权限级别" });
            cleaned[(e.SubjectType!, e.SubjectId!.Trim())] = (actions, e.InheritToChildren ?? true);
        }

        var names = await LoadSubjectNamesAsync(cleaned.Keys);
        if (cleaned.Keys.Any(k => !names.ContainsKey(k)))
            return BadRequest(new { message = "部分授权对象不存在或已被删除，请刷新后重试" });

        // 顶层文件夹没有上级，始终视为继承
        var inherit = request.InheritPermissions ?? t.InheritPermissions;
        if (t.Type == ResourceTypes.Folder && t.ParentFolderId == null) inherit = true;

        await using var tx = await _db.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;

        if (t.Type == ResourceTypes.Folder)
        {
            var folder = await _db.Folders.FirstAsync(f => f.Id == t.Id);
            folder.InheritPermissions = inherit;
            _db.FolderPermissions.RemoveRange(await _db.FolderPermissions.Where(p => p.FolderId == t.Id).ToListAsync());
            foreach (var ((st, sid), v) in cleaned)
                _db.FolderPermissions.Add(new FolderPermission
                {
                    FolderId = t.Id, SubjectType = st, SubjectId = sid, Actions = v.Actions,
                    InheritToChildren = v.Inherit, CreatedBy = userId, CreatedAt = now
                });
        }
        else
        {
            var doc = await _db.Documents.FirstAsync(d => d.Id == t.Id);
            doc.InheritPermissions = inherit;
            _db.DocumentPermissions.RemoveRange(await _db.DocumentPermissions.Where(p => p.DocumentId == t.Id).ToListAsync());
            foreach (var ((st, sid), v) in cleaned)
                _db.DocumentPermissions.Add(new DocumentPermission
                {
                    DocumentId = t.Id, SubjectType = st, SubjectId = sid, Actions = v.Actions,
                    CreatedBy = userId, CreatedAt = now
                });
        }
        await _db.SaveChangesAsync();

        // 防止非管理员把自己的管理权限设置没了（设置完就再也改不回来）
        _permissionService.ResetCache();
        if (!await _permissionService.CanAccessAsync(userId, t.Type, t.Id, PermissionActions.Manage))
        {
            await tx.RollbackAsync();
            _permissionService.ResetCache();
            return BadRequest(new { message = $"保存后你将失去对该{t.TypeName}的管理权限。请为自己保留“管理者”授权，或联系管理员操作" });
        }
        await tx.CommitAsync();

        var parts = new List<string>();
        if (inherit != t.InheritPermissions) parts.Add(inherit ? "恢复继承" : "断开继承");
        parts.Add(cleaned.Count == 0
            ? "无单独授权"
            : string.Join("；", cleaned.Select(kv => $"{names[kv.Key]}：{PermissionLevels.DisplayName(kv.Value.Actions)}"
                + (t.Type == ResourceTypes.Folder && !kv.Value.Inherit ? "（仅本文件夹）" : ""))));
        await _audit.LogAsync("设置权限", t.Type, t.Id, t.Name, string.Join("；", parts));

        return NoContent();
    }

    // ---------- 查询成员 ----------

    [HttpGet("{type}/{id:int}/check")]
    public async Task<IActionResult> Check(string type, int id, [FromQuery] int userId)
    {
        var (target, error) = await ResolveAsync(type, id);
        if (error != null) return error;

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Username, u.DisplayName })
            .FirstOrDefaultAsync();
        if (user == null) return NotFound(new { message = "成员不存在" });

        var result = await _permissionService.ExplainAsync(userId, target!.Type, id);
        var graph = await PermissionGraph.LoadAsync(_db);
        var names = await LoadSubjectNamesAsync(result.MatchedGrants.Select(g => (g.SubjectType, g.SubjectId)));

        return Ok(new
        {
            userName = string.IsNullOrEmpty(user.DisplayName) ? user.Username : $"{user.DisplayName}（{user.Username}）",
            result.IsAdmin,
            result.IsDisabled,
            actions = result.Actions,
            levelName = result.IsDisabled ? "无权限（账号已禁用）" : PermissionLevels.DisplayName(result.Actions),
            reasons = result.MatchedGrants.Select(g => ToDto(g, names, graph))
        });
    }

    // ---------- 总览 ----------

    /// <summary>列出所有断开继承或有单独授权的文件夹 / 文档（仅管理员）</summary>
    [HttpGet("overview")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Overview()
    {
        var graph = await PermissionGraph.LoadAsync(_db);
        var folderGrants = (await _db.FolderPermissions.AsNoTracking().ToListAsync()).ToLookup(g => g.FolderId);
        var docGrants = (await _db.DocumentPermissions.AsNoTracking().ToListAsync()).ToLookup(g => g.DocumentId);
        var docIds = docGrants.Select(g => g.Key).ToList();
        var docs = await _db.Documents.AsNoTracking()
            .Where(d => !d.InheritPermissions || docIds.Contains(d.Id))
            .Select(d => new { d.Id, d.FileName, d.FolderId, d.InheritPermissions })
            .ToListAsync();

        var names = await LoadSubjectNamesAsync(
            folderGrants.SelectMany(g => g).Select(g => (g.SubjectType, g.SubjectId))
                .Concat(docGrants.SelectMany(g => g).Select(g => (g.SubjectType, g.SubjectId))));

        object Grant(string st, string sid, PermissionActions a, bool? inheritToChildren) => new
        {
            subjectType = st,
            subjectName = names.GetValueOrDefault((st, sid), "（已删除）"),
            levelName = PermissionLevels.DisplayName(a),
            inheritToChildren
        };

        var items = new List<object>();
        foreach (var f in graph.Folders.Values
                     .Where(f => (!f.InheritPermissions && f.ParentId != null) || folderGrants[f.Id].Any())
                     .OrderBy(f => graph.PathOf(f.Id)))
        {
            items.Add(new
            {
                resourceType = ResourceTypes.Folder,
                resourceId = f.Id,
                name = f.Name,
                path = graph.PathOf(f.Id),
                folderId = f.Id,
                inheritPermissions = f.InheritPermissions || f.ParentId == null,
                grants = folderGrants[f.Id].Select(g => Grant(g.SubjectType, g.SubjectId, g.Actions, g.InheritToChildren))
            });
        }
        foreach (var d in docs.OrderBy(d => graph.PathOf(d.FolderId)).ThenBy(d => d.FileName))
        {
            items.Add(new
            {
                resourceType = ResourceTypes.Document,
                resourceId = d.Id,
                name = d.FileName,
                path = graph.PathOf(d.FolderId) + " / " + d.FileName,
                folderId = d.FolderId,
                inheritPermissions = d.InheritPermissions,
                grants = docGrants[d.Id].Select(g => Grant(g.SubjectType, g.SubjectId, g.Actions, null))
            });
        }
        return Ok(items);
    }

    // ---------- 辅助 ----------

    /// <summary>解析资源并校验管理权限</summary>
    private async Task<(Target?, IActionResult?)> ResolveAsync(string type, int id)
    {
        Target? target;
        switch (type.ToLowerInvariant())
        {
            case "folder":
                target = await _db.Folders.AsNoTracking().Where(f => f.Id == id)
                    .Select(f => new Target(ResourceTypes.Folder, f.Id, f.Name, f.Id, f.ParentId, f.InheritPermissions, "文件夹"))
                    .FirstOrDefaultAsync();
                break;
            case "document":
                target = await _db.Documents.AsNoTracking().Where(d => d.Id == id)
                    .Select(d => new Target(ResourceTypes.Document, d.Id, d.FileName, d.FolderId, null, d.InheritPermissions, "文档"))
                    .FirstOrDefaultAsync();
                break;
            default:
                return (null, NotFound(new { message = "接口不存在" }));
        }

        if (target == null) return (null, NotFound(new { message = type == "folder" ? "文件夹不存在" : "文档不存在" }));

        if (!await _permissionService.CanAccessAsync(User.GetUserId(), target.Type, id, PermissionActions.Manage))
            return (null, StatusCode(StatusCodes.Status403Forbidden,
                new { message = $"没有设置该{target.TypeName}权限的权限（需要管理权限）" }));

        return (target, null);
    }

    private static object ToDto(GrantInfo g, Dictionary<(string, string), string> names, PermissionGraph graph) => new
    {
        subjectType = g.SubjectType,
        subjectId = g.SubjectId,
        subjectName = names.GetValueOrDefault((g.SubjectType, g.SubjectId), "（已删除）"),
        actions = g.Actions,
        levelName = PermissionLevels.DisplayName(g.Actions),
        inheritToChildren = g.InheritToChildren,
        source = g.Source.ToString(),
        // 所有者（文件夹创建者）没有授权记录 ID
        isOwner = g.Source == GrantSource.Owner || (g.GrantId == null && g.FromFolderId != null),
        fromFolderId = g.FromFolderId,
        fromFolderName = g.FromFolderId is int fid ? graph.Folders.GetValueOrDefault(fid)?.Name : null
    };

    /// <summary>批量解析授权对象名称；不存在的对象不会出现在结果中</summary>
    private async Task<Dictionary<(string, string), string>> LoadSubjectNamesAsync(IEnumerable<(string Type, string Id)> subjects)
    {
        var list = subjects.Distinct().ToList();
        var result = new Dictionary<(string, string), string>();

        var userIds = list.Where(s => s.Type == SubjectTypes.User)
            .Select(s => int.TryParse(s.Id, out var v) ? v : -1).Where(v => v > 0).ToList();
        if (userIds.Count > 0)
        {
            var users = await _db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Username, u.DisplayName })
                .ToListAsync();
            foreach (var u in users)
                result[(SubjectTypes.User, u.Id.ToString())] = string.IsNullOrEmpty(u.DisplayName) ? u.Username : u.DisplayName;
        }

        var deptIds = list.Where(s => s.Type == SubjectTypes.Department)
            .Select(s => int.TryParse(s.Id, out var v) ? v : -1).Where(v => v > 0).ToList();
        if (deptIds.Count > 0)
        {
            var depts = await _db.Departments.AsNoTracking()
                .Where(d => deptIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Name })
                .ToListAsync();
            foreach (var d in depts)
                result[(SubjectTypes.Department, d.Id.ToString())] = d.Name;
        }

        // 角色：目前只开放“所有成员”
        if (list.Contains((SubjectTypes.Role, Roles.User)))
            result[(SubjectTypes.Role, Roles.User)] = "所有成员";

        return result;
    }
}
