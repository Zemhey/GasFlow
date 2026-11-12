using System.Security.Claims;

namespace GasInventory.Api.Services;

/// <summary>当前登录用户（来自 Cookie 里的声明，无状态读取）</summary>
public class 当前登录
{
    private readonly IHttpContextAccessor _http;

    public 当前登录(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? User => _http.HttpContext?.User;

    public bool 已登录 => User?.Identity?.IsAuthenticated == true;

    public int 员工编号
    {
        get
        {
            var v = User?.FindFirst("员工编号")?.Value;
            return int.TryParse(v, out var id) ? id : 0;
        }
    }

    public string 工号 => User?.FindFirst("工号")?.Value ?? "";
    public string 姓名 => User?.FindFirst("姓名")?.Value ?? "";
    public int 岗位编号
    {
        get
        {
            var v = User?.FindFirst("岗位编号")?.Value;
            return int.TryParse(v, out var id) ? id : 0;
        }
    }
}
