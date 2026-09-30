using DocumentsApp.Api.Models;

namespace DocumentsApp.Api.Services;

/// <summary>
/// 全放行实现（仅用于调试或单人使用）：所有权限检查永远返回 true。
/// 正式使用 <see cref="FolderPermissionService"/>。
/// </summary>
public class AdminPermissionService : IPermissionService
{
    public Task<bool> CanAccessAsync(int userId, string resourceType, int resourceId, PermissionActions action)
        => Task.FromResult(true);

    // null = 不过滤
    public Task<HashSet<int>?> GetAccessibleFolderIdsAsync(int userId)
        => Task.FromResult<HashSet<int>?>(null);

    public Task<PermissionActions> GetFolderActionsAsync(int userId, int folderId)
        => Task.FromResult(PermissionLevels.All);

    public Task<PermissionActions> GetDocumentActionsAsync(int userId, int documentId, int folderId, bool inheritPermissions)
        => Task.FromResult(PermissionLevels.All);

    public Task<HashSet<int>?> GetDocumentIdsGrantedDirectlyAsync(int userId)
        => Task.FromResult<HashSet<int>?>(null);

    public Task<bool> IsAdminAsync(int userId) => Task.FromResult(true);

    public Task<PermissionExplanation> ExplainAsync(int userId, string resourceType, int resourceId)
        => Task.FromResult(new PermissionExplanation { IsAdmin = true, Actions = PermissionLevels.All });

    public void ResetCache() { }
}
