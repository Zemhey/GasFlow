using System.Diagnostics;
using System.Text;
using GasInventory.Api.Services;
using GasInventory.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GasInventory.Api.Auth;

/// <summary>
/// 操作日志过滤器：把**真正改过数据的调用**写进 dbo.操作日志（走库里的 usp_审计_写操作日志）。
///
/// 为什么要有它：`/api/system/oplogs` 只是读接口，表里没有写入方就永远是 0 行，
/// 页面上"删除 / 冲销"这类追责依据也就无从谈起。阶段 7 的生成器**不伪造审计日志**（那是真实调用产生的），
/// 所以这一步补的是"调用发生时留下痕迹"的能力，而不是预生成数据。
///
/// 三条刻意的边界：
///   1. **只记写操作**（POST / PUT / PATCH / DELETE）。GET 是读，记了只会淹没真正要追责的行；
///      登录/登出已有 登录记录，这里整段跳过 /api/auth（含密码，绝不把口令写进 请求参数）。
///   2. **绝不阻塞业务**：审计写失败（库抖动 / 字段超长）只写一条 warning 日志，请求照常返回。
///   3. **请求参数脱敏 + 截断**：口令类字段一律打码，整体截到 2000 字符，避免把整份单据 JSON 灌进日志表。
/// </summary>
public class 审计过滤器 : IAsyncActionFilter
{
    /// <summary>控制器的英文名 → 中文模块名（与 前端 Oplogs.vue 的「业务模块」列一致）</summary>
    private static readonly Dictionary<string, string> 模块名 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Auth"] = "认证",
        ["Orders"] = "单据",
        ["Records"] = "档案",
        ["Cylinders"] = "钢瓶",
        ["Inventory"] = "库存",
        ["StockOps"] = "库存",
        ["PartnersReceivable"] = "往来",
        ["PartnersPayable"] = "往来",
        ["Reports"] = "报表",
        ["System"] = "系统",
        ["Lookups"] = "下拉",
        ["Dashboard"] = "看板",
        ["Dispatch"] = "配送",
    };

    /// <summary>单据动作 → 库里的操作类型（CHECK 约束只允许 查询/新增/修改/删除/审核/过账/打印/导出/登录/其他）</summary>
    private static readonly Dictionary<string, string> 动作类型 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["submit"] = "修改",
        ["approve"] = "审核",
        ["post"] = "过账",
        ["unpost"] = "过账",
        ["reverse"] = "修改",
    };

    /// <summary>
    /// 控制器 → 权限资源对象名（写进 操作日志.资源代码，库里是 varchar(60)）。
    /// 只登记"这个控制器整体归属哪个资源"，细到动作的差异由 功能/操作类型 表达。
    /// </summary>
    private static readonly Dictionary<string, string> 资源代码表 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Orders"] = "Sale.Delivery",
        ["Records"] = "Org.Dict",
        ["Cylinders"] = "Cyl.Archive",
        ["Inventory"] = "Inv.Stock",
        ["StockOps"] = "Inv.Transfer",
        ["PartnersReceivable"] = "Fin.Receivable",
        ["PartnersPayable"] = "Fin.Payable",
        ["Reports"] = "Rpt.Report",
        ["Dispatch"] = "Dist.Task",
    };

    /// <summary>个别"POST 其实是改"的动作（保存参数/保存矩阵是整批覆盖写，不是新增）</summary>
    private static readonly HashSet<string> 覆盖式保存 = new(StringComparer.Ordinal)
    {
        "保存参数", "保存矩阵"
    };

    private readonly 数据服务 _data;
    private readonly 当前登录 _me;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<审计过滤器> _log;

    public 审计过滤器(数据服务 data, 当前登录 me, IHttpContextAccessor http, ILogger<审计过滤器> log)
    {
        _data = data;
        _me = me;
        _http = http;
        _log = log;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var sw = Stopwatch.StartNew();
        var 已执行 = await next();
        sw.Stop();

        var req = ctx.HttpContext.Request;
        if (!要记(req)) return;
        if (!_me.已登录) return;          // 未登录的写操作由认证中间件拦掉，不该有日志

        try
        {
            var 控制器 = ctx.ActionDescriptor is ControllerActionDescriptor act ? act.ControllerName : "";
            var 模块 = 模块名.TryGetValue(控制器, out var m) ? m : 控制器;
            var 动作 = ctx.RouteData.Values.TryGetValue("动作", out var a) ? Convert.ToString(a) : null;
            var 类型 = 动作 is not null && 动作类型.TryGetValue(动作, out var t)
                ? t
                : req.Method switch
                {
                    "POST" => "新增",
                    "PUT" or "PATCH" => "修改",
                    "DELETE" => "删除",
                    _ => "其他"
                };
            // 有 id 的 POST/PUT 是"改"，没 id 才是"新增"（单据整单保存两种都走 POST）
            var 有主键 = ctx.RouteData.Values.ContainsKey("id");
            if (类型 == "新增" && 有主键) 类型 = "修改";

            // 成功与否**以响应码为准**（2xx 才算成功）。
            // 踩过两次坑：① 抛异常时 Response.StatusCode 还是 200（还没开始写响应）→ 出现"成功=false 但码=200"；
            // ② 业务拒绝（如状态机 409）不走异常，只看 Exception 会记成"成功=true"。
            var 码 = 状态码(ctx, 已执行);
            var 成功 = 码 is >= 200 and < 300;

            // 功能列：动作名（保存/删除/状态动作 等），取不到再退回"方法 + 控制器"
            var 动作名 = ctx.ActionDescriptor is ControllerActionDescriptor ca ? ca.ActionName : "";
            var 功能 = string.IsNullOrWhiteSpace(动作名) ? $"{req.Method} {控制器}" : 动作名;
            if (功能.Length > 80) 功能 = 功能[..80];

            // 整批覆盖式写（系统参数/权限矩阵）：语义上是"改"，不是"新增"
            if (覆盖式保存.Contains(功能)) 类型 = "修改";

            var 资源代码 = 资源代码表.TryGetValue(控制器, out var rc) ? rc : "";

            await _data.查询SqlAsync("""
                CALL public."usp_审计_写操作日志"(@e, @m, @f, @t, @rc, @url, @args, @code, @ok, @err, @ms, NULL, NULL, NULL, 'Web', @ip, @ua, NULL, @no)
                """, ctx.HttpContext.RequestAborted,
                ("@e", _me.员工编号),
                ("@m", 截断(模块, 40)),
                ("@f", 功能),
                ("@t", 类型),
                ("@rc", 资源代码.Length == 0 ? null : 资源代码),
                ("@url", 截断(请求地址(req), 500)),
                ("@args", 脱敏参数(ctx)),
                ("@code", 码),
                ("@ok", 成功),
                ("@err", 已执行.Exception is null ? null : 截断(已执行.Exception.Message, 500)),
                ("@ms", (int)Math.Min(sw.ElapsedMilliseconds, int.MaxValue)),
                ("@ip", 截断(客户端IP(), 64)),
                ("@ua", 截断(req.Headers.UserAgent.ToString(), 400)),
                ("@no", null));
        }
        catch (Exception ex)
        {
            // 审计是"增强"，不是"业务正确性"的一部分：写不进去也必须让请求成功返回
            _log.LogWarning(ex, "操作日志写入失败（不影响本次请求）：{Path}", req.Path);
        }
    }

    /// <summary>要记的请求：只记写操作，跳过认证（含口令）与健康检查</summary>
    private static bool 要记(HttpRequest req)
    {
        if (req.Method is not ("POST" or "PUT" or "PATCH" or "DELETE")) return false;
        if (req.Path.StartsWithSegments("/api/auth")) return false;
        if (req.Path.StartsWithSegments("/api/system/health")) return false;
        return true;
    }

    private static string 请求地址(HttpRequest req)
    {
        var sb = new StringBuilder(req.Path.ToString());
        if (req.QueryString.HasValue) sb.Append(req.QueryString.Value);
        return sb.ToString();
    }

    /// <summary>
    /// 请求参数：口令类字段打码，整体截到 2000 字符。
    /// 打码必须深入 JSON 串内部——按参数**名**打码只能挡住 `密码` 这个参数本身，
    /// 挡不住 `{ 密码哈希: "…", 密码盐: "…" }` 这类整对象（档案页改员工就会带上）。
    /// </summary>
    private static string? 脱敏参数(ActionExecutingContext ctx)
    {
        try
        {
            var 片段 = new List<string>();
            foreach (var (k, v) in ctx.ActionArguments)
            {
                var 值 = v is null ? "null" : 打码值(System.Text.Json.JsonSerializer.Serialize(v));
                if (值.Length > 800) 值 = 值[..800] + "…";
                片段.Add($"{k}={值}");
            }
            var 全部 = string.Join("; ", 片段);
            return 全部.Length == 0 ? null : (全部.Length > 2000 ? 全部[..2000] : 全部);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把 JSON 里 密码哈希/密码盐/密码/口令 之类键的值替换成 ***（保留键名便于追责）</summary>
    private static string 打码值(string json) =>
        敏感值正则.Replace(json, "\"${k}\":\"***\"");

    private static readonly System.Text.RegularExpressions.Regex 敏感值正则 = new(
        "\"(?<k>[^\"]*(?:密码|口令|password|Password|token|Token)[^\"]*)\"\\s*:\\s*(?:\"[^\"]*\"|[\\d.]+|true|false|null)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private string 客户端IP()
    {
        var h = _http.HttpContext;
        var 转发 = h?.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(转发)) return 转发.Split(',')[0].Trim();
        return h?.Connection.RemoteIpAddress?.ToString() ?? "本机";
    }

    /// <summary>
    /// 响应码：动作已经写了 Result（含 4xx/409）就取 Result 的，否则取 Response 的。
    /// 实测踩过：接口抛异常时 Response.StatusCode 仍是 200（还没开始写响应），
    /// 日志里就会出现"成功=false 但 响应码=200"这种自相矛盾的行。
    /// </summary>
    private static int 状态码(ActionExecutingContext ctx, ActionExecutedContext 已执行)
    {
        if (已执行.Result is ObjectResult o && o.StatusCode is { } s) return s;
        if (已执行.Result is StatusCodeResult sc) return sc.StatusCode;
        if (已执行.Exception is not null && !已执行.ExceptionHandled) return 500;
        var c = ctx.HttpContext.Response.StatusCode;
        return c == 0 ? 500 : c;
    }

    private static string? 截断(string? s, int n) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= n ? s : s[..n]);
}
