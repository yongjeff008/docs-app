using System.Security.Cryptography;
using DocumentsApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DocumentsApp.Api.Data;

/// <summary>
/// 数据库初始化：应用迁移 + 首次启动时创建初始管理员
/// 账号密码只存在数据库里（哈希），代码中不含任何默认密码。
/// 初始账号取自配置 InitialAdmin:Username / InitialAdmin:Password
/// （可用环境变量 InitialAdmin__Username / InitialAdmin__Password 传入）；
/// 未配置密码时随机生成，写入 initial-admin.txt，登录后请立即修改密码并删除该文件。
/// </summary>
public static class DbSeeder
{
    private const string InitialPasswordFile = "initial-admin.txt";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        // 应用 Code First 迁移（数据库不存在时自动创建）
        await db.Database.MigrateAsync();

        // 已有任意管理员就不再创建（管理员改名后也不会重复生成）
        if (await db.Users.AnyAsync(u => u.Role == Roles.Admin)) return;

        var username = config["InitialAdmin:Username"];
        if (string.IsNullOrWhiteSpace(username)) username = "admin";

        var password = config["InitialAdmin:Password"];
        var generated = string.IsNullOrEmpty(password);
        if (generated) password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12));

        db.Users.Add(new User
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password!),
            DisplayName = "管理员",
            Role = Roles.Admin,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        if (generated)
        {
            var path = Path.Combine(AppContext.BaseDirectory, InitialPasswordFile);
            await File.WriteAllTextAsync(path, $"用户名：{username}\n初始密码：{password}\n登录后请立即修改密码，并删除本文件。\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            logger.LogWarning("已创建初始管理员 {Username}，随机初始密码见 {Path}", username, path);
        }
        else
        {
            logger.LogInformation("已按配置创建初始管理员 {Username}", username);
        }
    }
}

/// <summary>
/// PBKDF2 密码哈希工具
/// 存储格式：迭代次数.盐(Base64).哈希(Base64)
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);
            // 固定时间比较，防止时序攻击
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
