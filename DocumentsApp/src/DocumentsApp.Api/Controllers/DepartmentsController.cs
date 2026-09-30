using DocumentsApp.Api.Data;
using DocumentsApp.Api.Models;
using DocumentsApp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocumentsApp.Api.Controllers;

/// <summary>
/// 部门管理（仅管理员）
/// </summary>
[ApiController]
[Route("api/departments")]
[Authorize(Roles = Roles.Admin)]
public class DepartmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogger _audit;

    public DepartmentsController(AppDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public record DepartmentRequest(string? Name);

    /// <summary>部门列表（含成员数）</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var counts = await _db.Users
            .Where(u => u.DepartmentId != null)
            .GroupBy(u => u.DepartmentId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count);

        var list = await _db.Departments.AsNoTracking()
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .ToListAsync();

        return Ok(list.Select(d => new { d.Id, d.Name, d.CreatedAt, UserCount = counts.GetValueOrDefault(d.Id) }));
    }

    /// <summary>新建部门</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DepartmentRequest request)
    {
        var (name, error) = ValidateName(request.Name);
        if (error != null) return BadRequest(new { message = error });
        if (await _db.Departments.AnyAsync(d => d.Name == name))
            return Conflict(new { message = $"部门“{name}”已存在" });

        var maxSort = await _db.Departments.Select(d => (int?)d.SortOrder).MaxAsync() ?? 0;
        var dept = new Department { Name = name!, SortOrder = maxSort + 1, CreatedAt = DateTime.UtcNow };
        _db.Departments.Add(dept);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("新建部门", "Department", dept.Id, dept.Name);
        return Ok(new { dept.Id, dept.Name });
    }

    /// <summary>重命名部门</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Rename(int id, [FromBody] DepartmentRequest request)
    {
        var dept = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id);
        if (dept == null) return NotFound(new { message = "部门不存在" });

        var (name, error) = ValidateName(request.Name);
        if (error != null) return BadRequest(new { message = error });
        if (await _db.Departments.AnyAsync(d => d.Name == name && d.Id != id))
            return Conflict(new { message = $"部门“{name}”已存在" });

        var oldName = dept.Name;
        dept.Name = name!;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("重命名部门", "Department", id, dept.Name, $"原名称：{oldName}");
        return NoContent();
    }

    /// <summary>删除部门（有成员时拒绝；同时删除该部门的文件夹授权）</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var dept = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id);
        if (dept == null) return NotFound(new { message = "部门不存在" });

        var userCount = await _db.Users.CountAsync(u => u.DepartmentId == id);
        if (userCount > 0)
            return Conflict(new { message = $"部门“{dept.Name}”下还有 {userCount} 名成员，请先将成员调整到其他部门" });

        var sid = id.ToString();
        var grants = await _db.FolderPermissions
            .Where(p => p.SubjectType == SubjectTypes.Department && p.SubjectId == sid)
            .ToListAsync();
        var docGrants = await _db.DocumentPermissions
            .Where(p => p.SubjectType == SubjectTypes.Department && p.SubjectId == sid)
            .ToListAsync();
        _db.FolderPermissions.RemoveRange(grants);
        _db.DocumentPermissions.RemoveRange(docGrants);
        _db.Departments.Remove(dept);
        await _db.SaveChangesAsync();

        var removed = grants.Count + docGrants.Count;
        await _audit.LogAsync("删除部门", "Department", id, dept.Name,
            removed > 0 ? $"同时移除 {removed} 条授权" : null);
        return NoContent();
    }

    private static (string? Name, string? Error) ValidateName(string? raw)
    {
        var name = raw?.Trim();
        if (string.IsNullOrEmpty(name)) return (null, "部门名称不能为空");
        if (name.Length > 64) return (null, "部门名称不能超过 64 个字符");
        return (name, null);
    }
}
