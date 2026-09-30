using System.Security.Claims;
using DocumentsApp.Api.Data;
using DocumentsApp.Api.Models;

namespace DocumentsApp.Api.Services;

/// <summary>
/// 操作日志记录器
/// 写日志失败只记录到应用日志，不影响业务流程。
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// 记录一条操作日志（操作人默认取当前登录用户，未登录场景可显式传入）
    /// </summary>
    Task LogAsync(string action, string? targetType = null, int? targetId = null, string? targetName = null,
        string? detail = null, int? userId = null, string? userName = null);
}

public class AuditLogger : IAuditLogger
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(AppDbContext db, IHttpContextAccessor http, ILogger<AuditLogger> logger)
    {
        _db = db;
        _http = http;
        _logger = logger;
    }

    public async Task LogAsync(string action, string? targetType = null, int? targetId = null, string? targetName = null,
        string? detail = null, int? userId = null, string? userName = null)
    {
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<AuditLog>? entry = null;
        try
        {
            var ctx = _http.HttpContext;
            var principal = ctx?.User;
            if (userId == null && principal?.Identity?.IsAuthenticated == true
                && int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                userId = uid;
            }
            if (userName == null && principal?.Identity?.IsAuthenticated == true)
            {
                var display = principal.FindFirstValue("display_name");
                var name = principal.FindFirstValue(ClaimTypes.Name);
                userName = string.IsNullOrEmpty(display) || display == name ? name : $"{display}（{name}）";
            }

            entry = _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                UserName = Truncate(userName ?? string.Empty, 128),
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                TargetName = targetName == null ? null : Truncate(targetName, 512),
                Detail = detail == null ? null : Truncate(detail, 1024),
                IpAddress = ctx?.Connection.RemoteIpAddress?.ToString(),
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // 与业务共用 DbContext：写入失败时移除跟踪，避免影响后续保存
            if (entry != null) entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            _logger.LogWarning(ex, "写入操作日志失败：{Action}", action);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
