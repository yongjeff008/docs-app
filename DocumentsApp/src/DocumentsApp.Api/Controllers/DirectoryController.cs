using DocumentsApp.Api.Data;
using DocumentsApp.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocumentsApp.Api.Controllers;

/// <summary>
/// 通讯录：设置文件夹权限时选择授权对象（所有登录用户可用）
/// </summary>
[ApiController]
[Route("api/directory")]
[Authorize]
public class DirectoryController : ControllerBase
{
    private readonly AppDbContext _db;

    public DirectoryController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var departments = await _db.Departments.AsNoTracking()
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .Select(d => new { d.Id, d.Name })
            .ToListAsync();

        // 管理员天然拥有全部权限，无需授权，不出现在列表中
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.IsEnabled && u.Role != Roles.Admin)
            .OrderBy(u => u.DisplayName)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.DisplayName,
                DepartmentName = u.Department != null ? u.Department.Name : null
            })
            .ToListAsync();

        return Ok(new { departments, users });
    }
}
