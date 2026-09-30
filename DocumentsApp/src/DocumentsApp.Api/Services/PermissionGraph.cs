using DocumentsApp.Api.Data;
using DocumentsApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DocumentsApp.Api.Services;

/// <summary>授权来源</summary>
public enum GrantSource
{
    /// <summary>在本文件夹 / 文档上直接设置</summary>
    Direct,
    /// <summary>从上级继承</summary>
    Inherited,
    /// <summary>所有者（文件夹创建者，自动拥有管理权限）</summary>
    Owner
}

/// <summary>
/// 一条适用于某资源的授权（含来源信息，用于判断与展示）
/// </summary>
public sealed record GrantInfo(
    string SubjectType,
    string SubjectId,
    PermissionActions Actions,
    bool InheritToChildren,
    GrantSource Source,
    int? GrantId,
    int? FromFolderId);

/// <summary>
/// 权限关系图：计算“某文件夹 / 文档上适用哪些授权、分别来自哪里”
///
/// 规则（与界面展示保持一致，权限判断与明细展示共用此类）：
/// - 文件夹 F 传给子级的授权 PassDown(F) = F 上“含子文件夹”的授权 + F 的所有者
///   + （F 继承时）PassDown(F 的父级)
/// - 文件夹 F 适用的授权 = F 自身全部授权 + F 的所有者 + （F 继承时）PassDown(父级)
/// - 文档 D 适用的授权 = D 自身授权 + （D 继承时）D 所在文件夹适用的全部授权
///   （“仅本文件夹”的授权对文件夹内的文档同样有效，只是不传给子文件夹）
/// - 断开继承 = 不再接收上级传来的授权，只保留自身授权（和所有者）
/// </summary>
public sealed class PermissionGraph
{
    public sealed record FolderInfo(int Id, string Name, int? ParentId, int CreatedBy, bool InheritPermissions);

    public IReadOnlyDictionary<int, FolderInfo> Folders { get; }

    private readonly ILookup<int, FolderPermission> _folderGrants;
    private readonly HashSet<int> _adminIds;
    private readonly Dictionary<int, List<GrantInfo>> _passDownCache = new();

    private PermissionGraph(Dictionary<int, FolderInfo> folders, ILookup<int, FolderPermission> grants, HashSet<int> adminIds)
    {
        Folders = folders;
        _folderGrants = grants;
        _adminIds = adminIds;
    }

    public static async Task<PermissionGraph> LoadAsync(AppDbContext db)
    {
        var folders = await db.Folders.AsNoTracking()
            .Select(f => new FolderInfo(f.Id, f.Name, f.ParentId, f.CreatedBy, f.InheritPermissions))
            .ToDictionaryAsync(f => f.Id);
        var grants = await db.FolderPermissions.AsNoTracking().ToListAsync();
        var adminIds = await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Admin)
            .Select(u => u.Id)
            .ToListAsync();
        return new PermissionGraph(folders, grants.ToLookup(g => g.FolderId), adminIds.ToHashSet());
    }

    /// <summary>文件夹自身的授权（直接设置 + 所有者）</summary>
    public List<GrantInfo> OwnForFolder(int folderId)
    {
        var list = new List<GrantInfo>();
        if (!Folders.TryGetValue(folderId, out var f)) return list;

        // 管理员本就拥有全部权限，不单独列为所有者
        if (f.CreatedBy > 0 && !_adminIds.Contains(f.CreatedBy))
            list.Add(new GrantInfo(SubjectTypes.User, f.CreatedBy.ToString(), PermissionLevels.Manager, true,
                GrantSource.Owner, null, folderId));

        list.AddRange(_folderGrants[folderId].Select(g => new GrantInfo(
            g.SubjectType, g.SubjectId, g.Actions, g.InheritToChildren, GrantSource.Direct, g.Id, folderId)));
        return list;
    }

    /// <summary>
    /// 文件夹从上级继承到的授权（已断开继承时为空）
    /// ignoreBreak = true 时忽略断开状态，返回“如果继承会得到什么”（用于断开时复制、恢复前预览）
    /// </summary>
    public List<GrantInfo> InheritedForFolder(int folderId, bool ignoreBreak = false)
    {
        if (!Folders.TryGetValue(folderId, out var f) || f.ParentId is not int pid) return new List<GrantInfo>();
        if (!f.InheritPermissions && !ignoreBreak) return new List<GrantInfo>();
        return PassDown(pid);
    }

    /// <summary>文件夹适用的全部授权</summary>
    public List<GrantInfo> AppliedToFolder(int folderId)
        => OwnForFolder(folderId).Concat(InheritedForFolder(folderId)).ToList();

    /// <summary>文档从所在文件夹继承到的授权（文档断开继承时由调用方忽略）</summary>
    public List<GrantInfo> InheritedForDocument(int folderId)
        => AppliedToFolder(folderId)
            .Select(g => g with { Source = GrantSource.Inherited })
            .ToList();

    /// <summary>文件夹传给子级的授权（来源统一标记为继承）</summary>
    private List<GrantInfo> PassDown(int folderId)
    {
        if (_passDownCache.TryGetValue(folderId, out var cached)) return cached;

        // 先占位，防御数据异常导致的环
        var result = new List<GrantInfo>();
        _passDownCache[folderId] = result;

        result.AddRange(OwnForFolder(folderId)
            .Where(g => g.InheritToChildren)
            .Select(g => g with { Source = GrantSource.Inherited }));

        if (Folders.TryGetValue(folderId, out var f) && f.InheritPermissions && f.ParentId is int pid)
            result.AddRange(PassDown(pid));

        return result;
    }

    /// <summary>从根到指定文件夹的名称路径，如“制度文件 / 采购”</summary>
    public string PathOf(int folderId)
    {
        var names = new List<string>();
        int? cursor = folderId;
        var guard = 0;
        while (cursor is int id && Folders.TryGetValue(id, out var f) && guard++ < 1000)
        {
            names.Insert(0, f.Name);
            cursor = f.ParentId;
        }
        return string.Join(" / ", names);
    }

    /// <summary>判断授权是否适用于某用户（本人 / 所在部门 / 角色）</summary>
    public static bool Matches(GrantInfo g, int userId, int? departmentId, string role)
        => g.SubjectType switch
        {
            SubjectTypes.User => g.SubjectId == userId.ToString(),
            SubjectTypes.Department => departmentId != null && g.SubjectId == departmentId.ToString(),
            SubjectTypes.Role => g.SubjectId == role,
            _ => false
        };
}
