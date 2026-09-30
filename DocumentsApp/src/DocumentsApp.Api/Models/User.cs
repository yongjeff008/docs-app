namespace DocumentsApp.Api.Models;

/// <summary>
/// 系统用户
/// </summary>
public class User
{
    public int Id { get; set; }

    /// <summary>登录名，如 "admin"</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>PBKDF2 哈希后的密码</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>显示名称，如 "管理员"</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>角色：见 <see cref="Roles"/></summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>所属部门（可为空）</summary>
    public int? DepartmentId { get; set; }

    public Department? Department { get; set; }

    /// <summary>是否启用（禁用后无法登录，已登录的会话在下一次请求时失效）</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>安全戳：修改 / 重置密码时更新，使该用户其他已登录会话失效</summary>
    public string SecurityStamp { get; set; } = string.Empty;

    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 角色常量
/// </summary>
public static class Roles
{
    /// <summary>系统管理员：拥有全部文件夹的全部权限，可管理用户与部门</summary>
    public const string Admin = "Admin";

    /// <summary>普通成员：按文件夹授权访问</summary>
    public const string User = "User";

    public static bool IsValid(string? role) => role is Admin or User;

    public static string DisplayName(string? role) => role switch
    {
        Admin => "管理员",
        User => "普通成员",
        _ => role ?? string.Empty
    };
}
