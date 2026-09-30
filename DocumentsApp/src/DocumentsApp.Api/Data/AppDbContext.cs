using DocumentsApp.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DocumentsApp.Api.Data;

/// <summary>
/// 应用数据库上下文（SQLite）
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<FolderPermission> FolderPermissions => Set<FolderPermission>();
    public DbSet<DocumentPermission> DocumentPermissions => Set<DocumentPermission>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite 不保存 DateTimeKind，读取时统一标记为 UTC，保证前端时间显示正确
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).HasMaxLength(64).IsRequired();
            e.Property(u => u.DisplayName).HasMaxLength(128);
            e.Property(u => u.Role).HasMaxLength(32);
            e.Property(u => u.SecurityStamp).HasMaxLength(64);
            // 删除部门时，成员的部门置空
            e.HasOne(u => u.Department)
                .WithMany()
                .HasForeignKey(u => u.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Department>(e =>
        {
            e.Property(d => d.Name).HasMaxLength(64).IsRequired();
            e.HasIndex(d => d.Name).IsUnique();
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.Property(a => a.UserName).HasMaxLength(128);
            e.Property(a => a.Action).HasMaxLength(64).IsRequired();
            e.Property(a => a.TargetType).HasMaxLength(32);
            e.Property(a => a.TargetName).HasMaxLength(512);
            e.Property(a => a.Detail).HasMaxLength(1024);
            e.Property(a => a.IpAddress).HasMaxLength(64);
            e.HasIndex(a => a.CreatedAt);
        });

        modelBuilder.Entity<Folder>(e =>
        {
            e.Property(f => f.Name).HasMaxLength(255).IsRequired();
            // 自关联：删除父文件夹时不级联（业务层要求先清空）
            e.HasOne(f => f.Parent)
                .WithMany(f => f.Children)
                .HasForeignKey(f => f.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(f => f.ParentId);
        });

        modelBuilder.Entity<Document>(e =>
        {
            e.Property(d => d.FileName).HasMaxLength(255).IsRequired();
            e.Property(d => d.StoredPath).HasMaxLength(512).IsRequired();
            e.Property(d => d.MimeType).HasMaxLength(128);
            e.HasOne(d => d.Folder)
                .WithMany()
                .HasForeignKey(d => d.FolderId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(d => d.FolderId);
        });

        modelBuilder.Entity<FolderPermission>(e =>
        {
            e.Property(p => p.SubjectType).HasMaxLength(32).IsRequired();
            e.Property(p => p.SubjectId).HasMaxLength(128).IsRequired();
            e.HasOne<Folder>()
                .WithMany()
                .HasForeignKey(p => p.FolderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.FolderId, p.SubjectType, p.SubjectId });
            e.HasIndex(p => new { p.SubjectType, p.SubjectId });
        });

        modelBuilder.Entity<DocumentPermission>(e =>
        {
            e.Property(p => p.SubjectType).HasMaxLength(32).IsRequired();
            e.Property(p => p.SubjectId).HasMaxLength(128).IsRequired();
            e.HasOne<Document>()
                .WithMany()
                .HasForeignKey(p => p.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => p.DocumentId);
            e.HasIndex(p => new { p.SubjectType, p.SubjectId });
        });
    }
}

/// <summary>
/// DateTime 与 UTC 的转换器
/// </summary>
public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
               v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}
