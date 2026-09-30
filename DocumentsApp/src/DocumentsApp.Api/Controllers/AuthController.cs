using System.Security.Claims;
using DocumentsApp.Api.Data;
using DocumentsApp.Api.Models;
using DocumentsApp.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocumentsApp.Api.Controllers;

/// <summary>
/// 认证接口（Cookie Session）
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogger _audit;

    public AuthController(AppDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public record LoginRequest(string? Username, string? Password);
    public record ChangePasswordRequest(string? OldPassword, string? NewPassword);

    /// <summary>登录：校验用户名密码，签发 7 天有效的 Cookie 票据</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
            return BadRequest(new { message = "请输入用户名和密码" });

        var username = request.Username.Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            await _audit.LogAsync("登录失败", "User", user?.Id, username, "用户名或密码错误", userId: user?.Id, userName: username);
            return Unauthorized(new { message = "用户名或密码错误" });
        }

        if (!user.IsEnabled)
        {
            await _audit.LogAsync("登录失败", "User", user.Id, username, "账号已禁用", userId: user.Id, userName: username);
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "账号已被禁用，请联系管理员" });
        }

        await SignInAsync(user);

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("登录", "User", user.Id, user.Username, userId: user.Id, userName: FormatUserName(user));

        return Ok(await ToMeDtoAsync(user));
    }

    /// <summary>退出登录</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    /// <summary>获取当前登录用户</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = User.GetUserId();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null || !user.IsEnabled)
            return Unauthorized(new { message = "登录已失效，请重新登录" });

        return Ok(await ToMeDtoAsync(user));
    }

    /// <summary>修改自己的密码（成功后其他设备上的登录会失效，当前会话保持登录）</summary>
    [HttpPost("password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == User.GetUserId());
        if (user == null) return Unauthorized(new { message = "登录已失效，请重新登录" });

        if (string.IsNullOrEmpty(request.OldPassword) || !PasswordHasher.Verify(request.OldPassword, user.PasswordHash))
            return BadRequest(new { message = "原密码不正确" });

        var error = PasswordRules.Validate(request.NewPassword);
        if (error != null) return BadRequest(new { message = error });
        if (request.NewPassword == request.OldPassword)
            return BadRequest(new { message = "新密码不能与原密码相同" });

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword!);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync();

        // 用新的安全戳重新签发当前会话
        await SignInAsync(user);
        await _audit.LogAsync("修改密码", "User", user.Id, user.Username);
        return NoContent();
    }

    // ---------- 辅助 ----------

    private Task SignInAsync(User user)
        => HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, UserPrincipalFactory.Create(user),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });

    private async Task<object> ToMeDtoAsync(User user)
    {
        var departmentName = user.DepartmentId == null
            ? null
            : await _db.Departments.Where(d => d.Id == user.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync();

        return new
        {
            id = user.Id,
            username = user.Username,
            displayName = user.DisplayName,
            role = user.Role,
            roleName = Roles.DisplayName(user.Role),
            isAdmin = user.Role == Roles.Admin,
            departmentId = user.DepartmentId,
            departmentName
        };
    }

    private static string FormatUserName(User u)
        => string.IsNullOrEmpty(u.DisplayName) || u.DisplayName == u.Username ? u.Username : $"{u.DisplayName}（{u.Username}）";
}

/// <summary>
/// 登录票据构造（登录签发与会话刷新共用）
/// </summary>
public static class UserPrincipalFactory
{
    public const string DisplayNameClaim = "display_name";
    public const string SecurityStampClaim = "security_stamp";

    public static ClaimsPrincipal Create(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(DisplayNameClaim, user.DisplayName),
            new(ClaimTypes.Role, user.Role),
            new(SecurityStampClaim, user.SecurityStamp)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}

/// <summary>
/// 密码规则
/// </summary>
public static class PasswordRules
{
    public const int MinLength = 6;

    /// <summary>校验通过返回 null，否则返回中文提示</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password)) return "请输入密码";
        if (password.Length < MinLength) return $"密码至少需要 {MinLength} 位";
        if (password.Length > 128) return "密码不能超过 128 位";
        return null;
    }
}

/// <summary>
/// 当前用户信息的便捷读取
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal principal)
        => int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
}
