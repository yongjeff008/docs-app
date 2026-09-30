using DocumentsApp.Api.Data;
using DocumentsApp.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocumentsApp.Api.Controllers;

/// <summary>
/// 操作日志查询（仅管理员）
/// </summary>
[ApiController]
[Route("api/audit-logs")]
[Authorize(Roles = Roles.Admin)]
public class AuditLogsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuditLogsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>分页查询：q 按操作人 / 操作 / 对象名称模糊匹配</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? q = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);

        var query = _db.AuditLogs.AsNoTracking();
        var keyword = q?.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            var pattern = $"%{EscapeLike(keyword)}%";
            query = query.Where(a => EF.Functions.Like(a.UserName, pattern, "\\")
                                     || EF.Functions.Like(a.Action, pattern, "\\")
                                     || (a.TargetName != null && EF.Functions.Like(a.TargetName, pattern, "\\")));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new { a.Id, a.UserName, a.Action, a.TargetType, a.TargetName, a.Detail, a.IpAddress, a.CreatedAt })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    private static string EscapeLike(string s)
        => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
