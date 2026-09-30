namespace DocumentsApp.Api.Models;

/// <summary>
/// 权限操作（位标志，可组合）
/// </summary>
[Flags]
public enum PermissionActions
{
    None = 0,
    /// <summary>查看：浏览文件夹、预览、下载</summary>
    Read = 1,
    /// <summary>写入：上传文档、新建子文件夹、重命名文档</summary>
    Write = 2,
    /// <summary>删除：删除文档</summary>
    Delete = 4,
    /// <summary>分享（预留，当前无分享功能）</summary>
    Share = 8,
    /// <summary>管理：重命名 / 移动 / 删除文件夹，设置文件夹权限</summary>
    Manage = 16
}

/// <summary>
/// 文件夹授权记录
/// 继承状态：有效权限 = 该文件夹自身的授权 ∪ 上级传递下来的授权（取并集）
/// 断开继承：有效权限 = 仅该文件夹自身的授权
/// </summary>
public class FolderPermission
{
    public int Id { get; set; }

    /// <summary>关联文件夹</summary>
    public int FolderId { get; set; }

    /// <summary>主体类型：见 <see cref="SubjectTypes"/></summary>
    public string SubjectType { get; set; } = string.Empty;

    /// <summary>主体标识：用户 ID / 部门 ID / 角色名</summary>
    public string SubjectId { get; set; } = string.Empty;

    public PermissionActions Actions { get; set; }

    /// <summary>是否向下继承到子文件夹</summary>
    public bool InheritToChildren { get; set; } = true;

    /// <summary>授权人</summary>
    public int CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 文档授权记录（文档断开继承后使用；继承状态下作为额外授权叠加）
/// </summary>
public class DocumentPermission
{
    public int Id { get; set; }

    public int DocumentId { get; set; }

    /// <summary>主体类型：见 <see cref="SubjectTypes"/></summary>
    public string SubjectType { get; set; } = string.Empty;

    /// <summary>主体标识：用户 ID / 部门 ID / 角色名</summary>
    public string SubjectId { get; set; } = string.Empty;

    public PermissionActions Actions { get; set; }

    /// <summary>授权人</summary>
    public int CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 授权主体类型
/// </summary>
public static class SubjectTypes
{
    public const string User = "User";
    public const string Department = "Department";
    /// <summary>角色：SubjectId 为 <see cref="Roles.User"/> 时表示“所有成员”</summary>
    public const string Role = "Role";

    public static bool IsValid(string? type) => type is User or Department or Role;
}

/// <summary>
/// 预设权限级别（界面上以级别展示，存储为位标志）
/// </summary>
public static class PermissionLevels
{
    public const PermissionActions Viewer = PermissionActions.Read;
    public const PermissionActions Uploader = Viewer | PermissionActions.Write;
    public const PermissionActions Editor = Uploader | PermissionActions.Delete;
    public const PermissionActions Manager = Editor | PermissionActions.Share | PermissionActions.Manage;

    /// <summary>全部有效位</summary>
    public const PermissionActions All = Manager;

    /// <summary>规范化：去掉无效位；只要有任何权限就必然包含“查看”</summary>
    public static PermissionActions Normalize(PermissionActions actions)
    {
        actions &= All;
        return actions == PermissionActions.None ? actions : actions | PermissionActions.Read;
    }

    public static string DisplayName(PermissionActions actions)
    {
        if (actions.HasFlag(PermissionActions.Manage)) return "管理者";
        if (actions.HasFlag(PermissionActions.Delete)) return "编辑者";
        if (actions.HasFlag(PermissionActions.Write)) return "上传者";
        if (actions.HasFlag(PermissionActions.Read)) return "查看者";
        return "无权限";
    }
}
