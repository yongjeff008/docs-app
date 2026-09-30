using DocumentsApp.Api.Data;
using DocumentsApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DocumentsApp.Api.Services;

/// <summary>
/// 基于文件夹 / 文档授权的权限实现
///
/// 规则：
/// 1. 管理员（Role = Admin）拥有全部权限；已禁用的用户没有任何权限。
/// 2. 授权主体：用户本人、所属部门、角色（Role:User 即“所有成员”）。
/// 3. 授权适用范围见 <see cref="PermissionGraph"/>：默认继承上级，多条授权取并集；
///    断开继承后只有自身授权生效，可随时恢复继承。
/// 4. 文件夹的创建者视为该文件夹（及其继承的子级）的管理者。
/// 5. 文档的上传者只要仍有该文档的上传（写入）权限，就可以删除自己上传的文档。
/// 6. 根目录：所有人可查看，只有管理员可以在根目录下新建 / 移入文件夹。
///
/// 服务为 Scoped，同一请求内计算结果会被缓存。
/// </summary>
public class FolderPermissionService : IPermissionService
{
    private readonly AppDbContext _db;
    private readonly Dictionary<int, UserPermissionContext?> _cache = new();
    private PermissionGraph? _graph;
    private Dictionary<int, List<DocumentPermission>>? _documentGrants;

    public FolderPermissionService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>单个用户的权限计算结果</summary>
    private sealed class UserPermissionContext
    {
        public bool IsAdmin { get; init; }
        public int? DepartmentId { get; init; }
        public string Role { get; init; } = string.Empty;

        /// <summary>文件夹 ID → 有效权限（只包含有权限的文件夹）</summary>
        public Dictionary<int, PermissionActions> FolderActions { get; } = new();

        /// <summary>文档 ID → 文档自身授权中适用于该用户的部分</summary>
        public Dictionary<int, PermissionActions> DocumentOwnActions { get; } = new();
    }

    public void ResetCache()
    {
        _cache.Clear();
        _graph = null;
        _documentGrants = null;
    }

    public async Task<bool> CanAccessAsync(int userId, string resourceType, int resourceId, PermissionActions action)
    {
        var ctx = await GetContextAsync(userId);
        if (ctx == null) return false;
        if (ctx.IsAdmin) return true;

        switch (resourceType)
        {
            case ResourceTypes.Folder:
                // 根目录：可查看，不可写入
                if (resourceId == 0) return action == PermissionActions.Read;
                return Has(ctx.FolderActions.GetValueOrDefault(resourceId), action);

            case ResourceTypes.Document:
                var doc = await _db.Documents.AsNoTracking()
                    .Where(d => d.Id == resourceId)
                    .Select(d => new { d.FolderId, d.CreatedBy, d.InheritPermissions })
                    .FirstOrDefaultAsync();
                // 文档不存在时放行，由 Controller 返回 404
                if (doc == null) return true;

                var actions = DocumentActions(ctx, resourceId, doc.FolderId, doc.InheritPermissions);
                if (Has(actions, action)) return true;
                // 上传者可删除自己上传的文档
                return action == PermissionActions.Delete
                       && doc.CreatedBy == userId
                       && actions.HasFlag(PermissionActions.Write);

            default:
                return false;
        }
    }

    public async Task<HashSet<int>?> GetAccessibleFolderIdsAsync(int userId)
    {
        var ctx = await GetContextAsync(userId);
        if (ctx == null) return new HashSet<int>();
        if (ctx.IsAdmin) return null;
        return ctx.FolderActions
            .Where(kv => kv.Value.HasFlag(PermissionActions.Read))
            .Select(kv => kv.Key)
            .ToHashSet();
    }

    public async Task<PermissionActions> GetFolderActionsAsync(int userId, int folderId)
    {
        var ctx = await GetContextAsync(userId);
        if (ctx == null) return PermissionActions.None;
        if (ctx.IsAdmin) return PermissionLevels.All;
        return ctx.FolderActions.GetValueOrDefault(folderId);
    }

    public async Task<PermissionActions> GetDocumentActionsAsync(int userId, int documentId, int folderId, bool inheritPermissions)
    {
        var ctx = await GetContextAsync(userId);
        if (ctx == null) return PermissionActions.None;
        if (ctx.IsAdmin) return PermissionLevels.All;
        return DocumentActions(ctx, documentId, folderId, inheritPermissions);
    }

    public async Task<HashSet<int>?> GetDocumentIdsGrantedDirectlyAsync(int userId)
    {
        var ctx = await GetContextAsync(userId);
        if (ctx == null) return new HashSet<int>();
        if (ctx.IsAdmin) return null;
        return ctx.DocumentOwnActions
            .Where(kv => kv.Value.HasFlag(PermissionActions.Read))
            .Select(kv => kv.Key)
            .ToHashSet();
    }

    public async Task<bool> IsAdminAsync(int userId)
        => (await GetContextAsync(userId))?.IsAdmin == true;

    public async Task<PermissionExplanation> ExplainAsync(int userId, string resourceType, int resourceId)
    {
        var ctx = await GetContextAsync(userId);
        if (ctx == null) return new PermissionExplanation { IsDisabled = true };
        if (ctx.IsAdmin) return new PermissionExplanation { IsAdmin = true, Actions = PermissionLevels.All };

        var graph = await GetGraphAsync();
        List<GrantInfo> applied;
        if (resourceType == ResourceTypes.Folder)
        {
            applied = graph.AppliedToFolder(resourceId);
        }
        else
        {
            var doc = await _db.Documents.AsNoTracking()
                .Where(d => d.Id == resourceId)
                .Select(d => new { d.FolderId, d.InheritPermissions })
                .FirstOrDefaultAsync();
            if (doc == null) return new PermissionExplanation();
            applied = (await GetDocumentGrantsAsync()).GetValueOrDefault(resourceId, new List<DocumentPermission>())
                .Select(g => new GrantInfo(g.SubjectType, g.SubjectId, g.Actions, false, GrantSource.Direct, g.Id, null))
                .ToList();
            if (doc.InheritPermissions)
                applied.AddRange(graph.InheritedForDocument(doc.FolderId));
        }

        var matched = applied.Where(g => PermissionGraph.Matches(g, userId, ctx.DepartmentId, ctx.Role)).ToList();
        var actions = matched.Aggregate(PermissionActions.None, (acc, g) => acc | g.Actions);
        return new PermissionExplanation { Actions = PermissionLevels.Normalize(actions), MatchedGrants = matched };
    }

    // ---------- 计算 ----------

    private static bool Has(PermissionActions actions, PermissionActions required)
        => required != PermissionActions.None && (actions & required) == required;

    private static PermissionActions DocumentActions(UserPermissionContext ctx, int documentId, int folderId, bool inherit)
    {
        var own = ctx.DocumentOwnActions.GetValueOrDefault(documentId);
        var inherited = inherit ? ctx.FolderActions.GetValueOrDefault(folderId) : PermissionActions.None;
        return PermissionLevels.Normalize(own | inherited);
    }

    private async Task<PermissionGraph> GetGraphAsync()
        => _graph ??= await PermissionGraph.LoadAsync(_db);

    private async Task<Dictionary<int, List<DocumentPermission>>> GetDocumentGrantsAsync()
        => _documentGrants ??= (await _db.DocumentPermissions.AsNoTracking().ToListAsync())
            .GroupBy(g => g.DocumentId)
            .ToDictionary(g => g.Key, g => g.ToList());

    private async Task<UserPermissionContext?> GetContextAsync(int userId)
    {
        if (_cache.TryGetValue(userId, out var cached)) return cached;

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.IsEnabled)
            .Select(u => new { u.Role, u.DepartmentId })
            .FirstOrDefaultAsync();

        if (user == null)
        {
            _cache[userId] = null;
            return null;
        }

        var ctx = new UserPermissionContext
        {
            IsAdmin = user.Role == Roles.Admin,
            DepartmentId = user.DepartmentId,
            Role = user.Role
        };

        if (!ctx.IsAdmin)
        {
            var graph = await GetGraphAsync();
            foreach (var folderId in graph.Folders.Keys)
            {
                var actions = PermissionActions.None;
                foreach (var g in graph.AppliedToFolder(folderId))
                    if (PermissionGraph.Matches(g, userId, user.DepartmentId, user.Role))
                        actions |= g.Actions;
                actions = PermissionLevels.Normalize(actions);
                if (actions != PermissionActions.None)
                    ctx.FolderActions[folderId] = actions;
            }

            foreach (var (docId, grants) in await GetDocumentGrantsAsync())
            {
                var actions = PermissionActions.None;
                foreach (var g in grants)
                {
                    var info = new GrantInfo(g.SubjectType, g.SubjectId, g.Actions, false, GrantSource.Direct, g.Id, null);
                    if (PermissionGraph.Matches(info, userId, user.DepartmentId, user.Role))
                        actions |= g.Actions;
                }
                if (actions != PermissionActions.None)
                    ctx.DocumentOwnActions[docId] = actions;
            }
        }

        _cache[userId] = ctx;
        return ctx;
    }
}
