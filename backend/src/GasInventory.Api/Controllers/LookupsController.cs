using GasInventory.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GasInventory.Api.Controllers;

/// <summary>
/// 下拉数据源：一次取全所有档案/字典，前端缓存到 Pinia，页面里用编号换名称。
/// 数据量很小，一次拉全比每个页面各查一次更快。
///
/// ⚠ 表清单必须是 **v3 真实表名**（且都在 `表元数据` 白名单里）：
///   v2 的 钢瓶规格/供气商/打气车/客户状态/结算方式/付款方式/结算账期/出货与送气分类/计提方案
///   在 v3 里已不存在（换成 商品/供应商/车辆/字典/客户字段），留着会让 /api/lookups 直接 500，
///   而它是**全局**下拉源 —— 一挂就是所有页面都拉不到字典。
/// </summary>
[ApiController]
[Route("api/lookups")]
[Authorize]
public class LookupsController : ControllerBase
{
    private static readonly string[] 表清单 =
    {
        "客户", "客户分类", "客户区域", "渠道", "客户协议价",
        "员工", "部门", "岗位",
        "商品", "商品品类", "商品条码",
        "供应商", "车辆", "公司", "经营网点", "仓库",
        "价格方案", "字典类型", "字典项", "计量单位", "费用类型",
        "权限项", "权限资源"
    };

    private readonly 数据服务 _data;

    public LookupsController(数据服务 data) => _data = data;

    [HttpGet]
    public async Task<IActionResult> 全部(CancellationToken ct)
    {
        var result = new Dictionary<string, object>();
        foreach (var t in 表清单)
        {
            var r = await _data.查询Async(t, new Dictionary<string, string?>(), null, 1, 500, null, false, null, ct);
            result[t] = r.数据;
        }
        return Ok(result);
    }
}
