using System.Security.Claims;
using DocumentsApp.Api.Controllers;
using DocumentsApp.Api.Data;
using DocumentsApp.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- 上传大小限制 ----------
var maxFileSizeMb = builder.Configuration.GetValue("Storage:MaxFileSizeMB", 100);
// 请求体上限比单文件上限多留 10MB 给表单字段与边界
var maxRequestBytes = (maxFileSizeMb + 10) * 1024L * 1024L;
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxRequestBytes);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxRequestBytes);

// ---------- 数据库 ----------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=documents.db"));

// ---------- 认证（Cookie） ----------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "DocumentsApp.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.LoginPath = "/login.html";

        // API 请求未登录时返回 401/403 JSON，而不是 302 跳转
        options.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return ctx.Response.WriteAsJsonAsync(new { message = "未登录或登录已过期，请重新登录" });
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return ctx.Response.WriteAsJsonAsync(new { message = "没有权限执行此操作" });
        };

        // 每次请求校验账号状态：已删除 / 已禁用 / 密码已修改的会话立即失效；角色、姓名变更后自动刷新票据
        options.Events.OnValidatePrincipal = async ctx =>
        {
            var principal = ctx.Principal;
            var userId = principal?.GetUserId() ?? 0;
            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            var stamp = principal?.FindFirstValue(UserPrincipalFactory.SecurityStampClaim) ?? string.Empty;

            if (principal == null || user == null || !user.IsEnabled || user.SecurityStamp != stamp)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            if (principal.FindFirstValue(ClaimTypes.Role) != user.Role
                || principal.FindFirstValue(UserPrincipalFactory.DisplayNameClaim) != user.DisplayName)
            {
                ctx.ReplacePrincipal(UserPrincipalFactory.Create(user));
                ctx.ShouldRenew = true;
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

// ---------- 业务服务 ----------
// ⭐ 权限实现：按文件夹授权 + 向下继承（调试时可换成 AdminPermissionService 全放行）
builder.Services.AddScoped<IPermissionService, FolderPermissionService>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddSingleton<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IPreviewService, LibreOfficePreviewService>();

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // 模型校验失败时返回统一的中文提示
        options.InvalidModelStateResponseFactory = _ =>
            new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { message = "请求参数格式不正确" });
    });

var app = builder.Build();

// 启动时自动建库 + 首次启动创建初始管理员
await DbSeeder.SeedAsync(app.Services);

// 经本机反向代理（如 Tailscale Funnel）访问时，从 X-Forwarded-For 取真实客户端 IP（默认只信任本机代理）
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// 全局异常兜底：API 返回中文 JSON 错误
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async ctx =>
    {
        var feature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalException");
        if (feature?.Error is BadHttpRequestException badRequest &&
            badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await ctx.Response.WriteAsJsonAsync(new { message = $"文件过大，单个文件不能超过 {maxFileSizeMb} MB" });
            return;
        }
        logger.LogError(feature?.Error, "未处理的异常");
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await ctx.Response.WriteAsJsonAsync(new { message = "服务器内部错误，请稍后重试" });
    });
});

app.UseAuthentication();

// 未登录访问主页 / 管理页时跳转到登录页
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "/";
    if ((path == "/" || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase)
                     || path.Equals("/admin.html", StringComparison.OrdinalIgnoreCase))
        && ctx.User.Identity?.IsAuthenticated != true)
    {
        ctx.Response.Redirect("/login.html");
        return;
    }
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // 前端无构建步骤，禁止缓存 HTML，便于更新后立即生效
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.CacheControl = "no-cache";
    }
});

app.UseAuthorization();
app.MapControllers();

// 未匹配的 API 返回 404 JSON
app.Map("/api/{**rest}", (HttpContext ctx) =>
    Results.Json(new { message = "接口不存在" }, statusCode: StatusCodes.Status404NotFound));

app.Run();
