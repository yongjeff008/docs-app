using System.Text.RegularExpressions;
using DocumentsApp.Api.Data;
using DocumentsApp.Api.Models;
using DocumentsApp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocumentsApp.Api.Controllers;

/// <summary>
/// 用户管理（仅管理员）
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = Roles.Admin)]
public partial class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogger _audit;

    public UsersController(AppDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public record CreateUserRequest(string? Username, string? DisplayName, string? Password, string? Role, int? DepartmentId);
    public record UpdateUserRequest(string? DisplayName, string? Role, int? DepartmentId, bool? IsEnabled);
    public record ResetPasswordRequest(string? Password);

    [GeneratedRegex("^[A-Za-z0-9_.-]{2,32}$")]
    private static partial Regex UsernamePattern();

    /// <summary>用户列表</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var users = await _db.Users.AsNoTracking()
            .OrderByDescending(u => u.Role == Roles.Admin)
            .ThenBy(u => u.Username)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.DisplayName,
                u.Role,
                u.DepartmentId,
                DepartmentName = u.Department != null ? u.Department.Name : null,
                u.IsEnabled,
                u.LastLoginAt,
                u.CreatedAt
            })
            .ToListAsync();

        return Ok(users.Select(u => new
        {
            u.Id,
            u.Username,
            u.DisplayName,
            u.Role,
            RoleName = Roles.DisplayName(u.Role),
            u.DepartmentId,
            u.DepartmentName,
            u.IsEnabled,
            u.LastLoginAt,
            u.CreatedAt
        }));
    }

    /// <summary>新建用户</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        if (!UsernamePattern().IsMatch(username))
            return BadRequest(new { message = "用户名须为 2-32 位字母、数字或 _ . -" });

        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrEmpty(displayName)) return BadRequest(new { message = "请输入姓名" });
        if (displayName.Length > 64) return BadRequest(new { message = "姓名不能超过 64 个字符" });

        var pwdError = PasswordRules.Validate(request.Password);
        if (pwdError != null) return BadRequest(new { message = pwdError });

        var role = string.IsNullOrEmpty(request.Role) ? Roles.User : request.Role;
        if (!Roles.IsValid(role)) return BadRequest(new { message = "角色无效" });

        if (request.DepartmentId is int deptId && !await _db.Departments.AnyAsync(d => d.Id == deptId))
            return BadRequest(new { message = "所选部门不存在" });

        // SQLite 默认区分大小写，这里统一按不区分大小写判重
        var lower = username.ToLower();
        if (await _db.Users.AnyAsync(u => u.Username.ToLower() == lower))
            return Conflict(new { message = $"用户名“{username}”已存在" });

        var user = new User
        {
            Username = username,
            DisplayName = displayName,
            PasswordHash = PasswordHasher.Hash(request.Password!),
            Role = role,
            DepartmentId = request.DepartmentId,
            IsEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("新建用户", "User", user.Id, user.Username, $"角色：{Roles.DisplayName(role)}");
        return Ok(new { user.Id, user.Username });
    }

    /// <summary>修改用户信息（姓名 / 角色 / 部门 / 启用状态）</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound(new { message = "用户不存在" });

        var isSelf = id == User.GetUserId();
        var changes = new List<string>();

        if (request.DisplayName != null)
        {
            var displayName = request.DisplayName.Trim();
            if (displayName.Length == 0) return BadRequest(new { message = "姓名不能为空" });
            if (displayName.Length > 64) return BadRequest(new { message = "姓名不能超过 64 个字符" });
            if (displayName != user.DisplayName) changes.Add($"姓名：{user.DisplayName} → {displayName}");
            user.DisplayName = displayName;
        }

        if (request.Role != null && request.Role != user.Role)
        {
            if (!Roles.IsValid(request.Role)) return BadRequest(new { message = "角色无效" });
            if (isSelf) return BadRequest(new { message = "不能修改自己的角色" });
            if (user.Role == Roles.Admin && !await HasOtherEnabledAdminAsync(id))
                return BadRequest(new { message = "至少需要保留一个启用的管理员" });
            changes.Add($"角色：{Roles.DisplayName(user.Role)} → {Roles.DisplayName(request.Role)}");
            user.Role = request.Role;
        }

        // DepartmentId：前端总是提交当前值，null 表示“无部门”
        if (request.DepartmentId != user.DepartmentId)
        {
            if (request.DepartmentId is int deptId && !await _db.Departments.AnyAsync(d => d.Id == deptId))
                return BadRequest(new { message = "所选部门不存在" });
            changes.Add("调整部门");
            user.DepartmentId = request.DepartmentId;
        }

        if (request.IsEnabled is bool enabled && enabled != user.IsEnabled)
        {
            if (!enabled && isSelf) return BadRequest(new { message = "不能禁用自己的账号" });
            if (!enabled && user.Role == Roles.Admin && !await HasOtherEnabledAdminAsync(id))
                return BadRequest(new { message = "至少需要保留一个启用的管理员" });
            changes.Add(enabled ? "启用账号" : "禁用账号");
            user.IsEnabled = enabled;
        }

        await _db.SaveChangesAsync();
        if (changes.Count > 0)
            await _audit.LogAsync("修改用户", "User", user.Id, user.Username, string.Join("；", changes));
        return NoContent();
    }

    /// <summary>重置密码（该用户所有已登录会话失效）</summary>
    [HttpPut("{id:int}/password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound(new { message = "用户不存在" });
        if (id == User.GetUserId())
            return BadRequest(new { message = "请通过右上角“修改密码”修改自己的密码" });

        var error = PasswordRules.Validate(request.Password);
        if (error != null) return BadRequest(new { message = error });

        user.PasswordHash = PasswordHasher.Hash(request.Password!);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync();

        await _audit.LogAsync("重置密码", "User", user.Id, user.Username);
        return NoContent();
    }

    /// <summary>删除用户（同时删除其所有文件夹授权；其上传的文档保留）</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound(new { message = "用户不存在" });
        if (id == User.GetUserId()) return BadRequest(new { message = "不能删除自己的账号" });
        if (user.Role == Roles.Admin && !await HasOtherEnabledAdminAsync(id))
            return BadRequest(new { message = "至少需要保留一个启用的管理员" });

        var sid = id.ToString();
        var grants = await _db.FolderPermissions
            .Where(p => p.SubjectType == SubjectTypes.User && p.SubjectId == sid)
            .ToListAsync();
        var docGrants = await _db.DocumentPermissions
            .Where(p => p.SubjectType == SubjectTypes.User && p.SubjectId == sid)
            .ToListAsync();
        _db.FolderPermissions.RemoveRange(grants);
        _db.DocumentPermissions.RemoveRange(docGrants);
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        var removed = grants.Count + docGrants.Count;
        await _audit.LogAsync("删除用户", "User", id, user.Username,
            removed > 0 ? $"同时移除 {removed} 条授权" : null);
        return NoContent();
    }

    private Task<bool> HasOtherEnabledAdminAsync(int excludeId)
        => _db.Users.AnyAsync(u => u.Id != excludeId && u.Role == Roles.Admin && u.IsEnabled);
}
