using GasInventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GasInventory.Api.Auth;

/// <summary>标注控制器对应的权限资源（对应《权限资源.对象名》）</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class 资源Attribute : Attribute
{
    public 资源Attribute(string name) => 对象名 = name;
    public string 对象名 { get; }
}

/// <summary>标注接口需要的权限代码（对应《权限项.权限代码》，VIEW/ADD/EDIT/DEL/AUDIT/POST/PRINT/EXPORT），缺省 VIEW</summary>
[AttributeUsage(AttributeTargets.Method)]
public class 需要权限Attribute : Attribute
{
    public 需要权限Attribute(string code = "VIEW") => 权限代码 = code;
    public string 权限代码 { get; }
}

/// <summary>
/// 资源对象名由**路由参数**决定（批次 20）：`/api/orders/{kind}` → 映射集「单据」，
/// `/api/records/{table}` → 映射集「档案」。映射表在 <see cref="Services.动态资源表"/>，
/// 与静态 `[资源]` 的区别只有"名字从路由里查"这一条 —— 判不出的 kind（未知单据类型）
/// 不在这里拦，仍由控制器回 400，避免"参数写错"被伪装成"没权限"。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class 资源按路由Attribute : Attribute
{
    public 资源按路由Attribute(string 参数名, string 映射集)
    {
        参数 = 参数名;
        映射 = 映射集;
    }

    public string 参数 { get; }
    public string 映射 { get; }
}

/// <summary>权限代码由路由参数决定（单据动作：submit→edit、approve→audit、post/qc/reverse→post）</summary>
[AttributeUsage(AttributeTargets.Method)]
public class 需要权限按路由Attribute : Attribute
{
    public 需要权限按路由Attribute(string 参数名, string 映射集)
    {
        参数 = 参数名;
        映射 = 映射集;
    }

    public string 参数 { get; }
    public string 映射 { get; }
}

/// <summary>
/// 全局权限过滤器：前端藏菜单只是体验，真正的拦截在这里。
/// 未登录交给 Cookie 认证（401）；已登录但无授权返回 403。
/// </summary>
public class 权限过滤器 : IAsyncActionFilter
{
    private readonly 权限服务 _perm;
    private readonly 当前登录 _me;

    public 权限过滤器(权限服务 perm, 当前登录 me)
    {
        _perm = perm;
        _me = me;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var ep = ctx.HttpContext.GetEndpoint();
        if (ep?.Metadata.GetMetadata<IAllowAnonymous>() is not null) { await next(); return; }

        if (ctx.ActionDescriptor is not ControllerActionDescriptor act) { await next(); return; }

        // 资源：**方法级优先**，撤不到再取类级。
        // 一个控制器下不同接口常归不同资源（系统参数是 Org.Param、操作日志是 Sys.Audit），
        // 只有类级就无法表达，只能退而求其次地给它一个最宽的资源 —— 那等于放宽权限。
        var 特性 = act.MethodInfo.GetCustomAttributes(inherit: true);

        // 资源：**方法级 [资源] 优先 → 类级 [资源] → 方法级 [资源按路由]（批次 20 补）**。
        // 一个控制器下不同接口常归不同资源（系统参数是 Org.Param、操作日志是 Sys.Audit），
        // 只有类级就无法表达，只能退而求其次地给它一个最宽的资源 —— 那等于放宽权限。
        var 资源 = 特性.OfType<资源Attribute>().FirstOrDefault()?.对象名
            ?? act.ControllerTypeInfo
                .GetCustomAttributes(typeof(资源Attribute), true)
                .OfType<资源Attribute>()
                .FirstOrDefault()?.对象名;

        if (资源 is null)
        {
            // 路由驱动的资源：/orders/{kind}、/records/{table} 这类"一个方法管一族"的接口。
            // 批次 20 之前它们各自在控制器里手写校验，读接口（列表/详情/取价）干脆没判 → 任何登录用户可翻全部单据。
            var 按路由 = 特性.OfType<资源按路由Attribute>().FirstOrDefault();
            if (按路由 is not null)
                资源 = Services.动态资源表.资源(按路由.映射, 路由值(ctx, 按路由.参数));
        }

        if (资源 is null) { await next(); return; }      // 未声明资源的接口（如登录/下拉数据）不做细粒度校验
        if (!_me.已登录) { await next(); return; }        // 未登录由认证中间件处理

        // 权限代码：**按路由（单据动作）优先**，再取方法级 [需要权限]，都缺省 VIEW
        var 代码特性 = 特性.OfType<需要权限按路由Attribute>().FirstOrDefault();
        var 权限代码 = (代码特性 is not null
                ? Services.动态资源表.权限代码(代码特性.映射, 路由值(ctx, 代码特性.参数))
                : null)
            ?? 特性.OfType<需要权限Attribute>().FirstOrDefault()?.权限代码 ?? "VIEW";

        if (!await _perm.有权限Async(_me.员工编号, 资源, 权限代码))
        {
            ctx.Result = new ObjectResult(new { message = $"没有权限：{资源} / {权限代码}" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }

    /// <summary>取路由参数值（kind / table / 动作）；控制器方法形参名与路由占位符同名，RouteData 里一定有</summary>
    private static string? 路由值(ActionExecutingContext ctx, string 参数) =>
        ctx.RouteData.Values.TryGetValue(参数, out var v) ? Convert.ToString(v) : null;
}
