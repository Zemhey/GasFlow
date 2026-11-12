using System.Text.Encodings.Web;
using System.Text.Unicode;
using GasInventory.Api.Auth;
using GasInventory.Api.Services;
using GasInventory.Infrastructure.Data;
using GasInventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var conn = builder.Configuration.GetConnectionString("Default")
           ?? "Host=localhost;Port=55432;Database=gasinventory;Username=postgres;Password=testpwd";

builder.Services.AddDbContext<GasInventoryDbContext>(o =>
    o.UseNpgsql(conn, pg => pg.CommandTimeout(60)));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<数据服务>();
builder.Services.AddScoped<单号服务>();
builder.Services.AddScoped<权限服务>();
builder.Services.AddScoped<登录服务>();
builder.Services.AddScoped<当前登录>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "gas.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(10);
        o.SlidingExpiration = true;
        // 接口返回 401 而不是跳转登录页，前端统一拦截
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers(o =>
    {
        // 顺序有意义：先判权限（没权限就不该留"他试图改"的误导性日志），
        // 再记审计（记的是真正被放行执行的写操作及其成败）。
        o.Filters.Add<权限过滤器>();
        o.Filters.Add<审计过滤器>();
    })
    .AddJsonOptions(o =>
    {
        // 中文不转义、中文属性名原样输出（实体与 DTO 均用中文字段名）
        o.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        o.JsonSerializerOptions.PropertyNamingPolicy = null;
        o.JsonSerializerOptions.DictionaryKeyPolicy = null;
    });

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddCors(o => o.AddPolicy("dev", p => p
    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// 首次部署：v3 种子里员工密码为空（"尚未开通登录"），登录前按参数 sys.初始密码 开通一次。
// 只补"密钥全空"的员工，不会覆盖任何已改过的密码；失败不阻塞启动（登录时会给出明确原因）。
if (!args.Contains("--no-init-passwords"))
{
    using var scope = app.Services.CreateScope();
    try
    {
        var n = await scope.ServiceProvider.GetRequiredService<登录服务>().初始化密码Async();
        if (n > 0) app.Logger.LogInformation("已为 {N} 个未开通登录的员工设置初始密码", n);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "初始密码初始化失败（不影响启动，登录时会提示原因）");
    }

    // 资源登记自检：登记了库里不存在的对象名，接口会整族 403 —— 启动时就喊出来。
    // 三处来源一起查（批次 20）：路由资源表（页面）+ 动态资源表（kind/table 驱动）+ 控制器上的 [资源]（类级与方法级）。
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<GasInventoryDbContext>();
        var 库内 = await db.权限资源.AsNoTracking().Where(r => r.对象名 != null)
            .Select(r => r.对象名!).ToListAsync();

        var 登记 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var 名 in 路由资源表.全部.Values.Concat(动态资源表.全部资源名()))
            if (!string.IsNullOrWhiteSpace(名)) 登记.Add(名.Trim());
        foreach (var 型 in typeof(Program).Assembly.GetTypes()
                     .Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t)))
        {
            foreach (var a in 型.GetCustomAttributes(typeof(资源Attribute), true).Cast<资源Attribute>()) 登记.Add(a.对象名);
            foreach (var m in 型.GetMethods().SelectMany(m => m.GetCustomAttributes(typeof(资源Attribute), true).Cast<资源Attribute>()))
                登记.Add(m.对象名);
        }

        var 缺失 = 登记.Where(x => !库内.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (缺失.Count > 0)
            app.Logger.LogWarning("资源登记/控制器 [资源] 里有 {N} 个对象名在 权限资源 表里不存在：{列表}",
                缺失.Count, string.Join(", ", 缺失));
        else
            app.Logger.LogInformation("资源自检通过（{N} 条路由 + {M} 个动态映射 + 控制器 [资源]，共 {T} 个对象名）",
                路由资源表.全部.Count, 动态资源表.全部资源名().Count(), 登记.Count);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "路由资源登记自检跳过（数据库不可用）");
    }
}

app.UseCors("dev");
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// 前端打包产物由同一进程托管，刷新子路由不 404
var spa = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
if (File.Exists(spa)) app.MapFallbackToFile("index.html");

app.Run();
