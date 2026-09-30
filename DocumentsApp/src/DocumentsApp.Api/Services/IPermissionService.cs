using DocumentsApp.Api.Models;

namespace DocumentsApp.Api.Services;

/// <summary>
/// 权限服务
/// 当前实现为 <see cref="FolderPermissionService"/>（文件夹 / 文档授权 + 继承 / 断开继承），
/// 替换实现时只需修改 Program.cs 中的 DI 注册。
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// 检查用户对某资源是否有某操作权限
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="resourceType">资源类型：见 <see cref="ResourceTypes"/></param>
    /// <param name="resourceId">资源 ID（根目录以 0 表示）</param>
    /// <param name="action">操作类型</param>
    Task<bool> CanAccessAsync(int userId, string resourceType, int resourceId, PermissionActions action);

    /// <summary>
    /// 获取用户可见（有查看权限）的文件夹 ID 集合，用于过滤树与搜索
    /// 返回 null 表示不过滤（全部可见，如管理员）
    /// </summary>
    Task<HashSet<int>?> GetAccessibleFolderIdsAsync(int userId);

    /// <summary>获取用户对某文件夹的有效权限（供前端控制按钮显示）</summary>
    Task<PermissionActions> GetFolderActionsAsync(int userId, int folderId);

    /// <summary>获取用户对某文档的有效权限（列表场景由调用方提供文档所在文件夹与继承状态，避免逐个查询）</summary>
    Task<PermissionActions> GetDocumentActionsAsync(int userId, int documentId, int folderId, bool inheritPermissions);

    /// <summary>
    /// 通过文档自身授权获得查看权限的文档 ID（用于搜索：文件夹无权限但文档单独授权给了用户）
    /// 返回 null 表示不过滤（管理员）
    /// </summary>
    Task<HashSet<int>?> GetDocumentIdsGrantedDirectlyAsync(int userId);

    /// <summary>用户是否为管理员（已禁用的用户返回 false）</summary>
    Task<bool> IsAdminAsync(int userId);

    /// <summary>解释用户对某资源的最终权限及其来源（权限明细“查询成员”使用）</summary>
    Task<PermissionExplanation> ExplainAsync(int userId, string resourceType, int resourceId);

    /// <summary>清空本请求内的计算缓存（修改授权后重新计算时使用）</summary>
    void ResetCache();
}

/// <summary>
/// 权限解释结果
/// </summary>
public sealed class PermissionExplanation
{
    public PermissionActions Actions { get; init; }

    public bool IsAdmin { get; init; }

    /// <summary>账号不存在或已禁用</summary>
    public bool IsDisabled { get; init; }

    /// <summary>对该用户生效的授权（含来源）</summary>
    public List<GrantInfo> MatchedGrants { get; init; } = new();
}

/// <summary>
/// 资源类型常量
/// </summary>
public static class ResourceTypes
{
    public const string Folder = "Folder";
    public const string Document = "Document";
}
