namespace DocumentsApp.Api.Models;

/// <summary>
/// 操作日志（登录、上传、下载、删除、授权变更等）
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>操作人 ID（登录失败等场景可能为空）</summary>
    public int? UserId { get; set; }

    /// <summary>操作人名称（冗余保存，用户删除后仍可追溯）</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>操作名称，如“上传文档”</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>对象类型：Folder / Document / User / Department</summary>
    public string? TargetType { get; set; }

    public int? TargetId { get; set; }

    /// <summary>对象名称（冗余保存）</summary>
    public string? TargetName { get; set; }

    /// <summary>补充说明</summary>
    public string? Detail { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; }
}
