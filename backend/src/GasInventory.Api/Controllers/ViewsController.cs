using GasInventory.Api.Auth;
using GasInventory.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GasInventory.Api.Controllers;

/// <summary>
/// 报表 / 台账通用读接口：直接读 v3 的 120 个 `vw_*` 视图（已迁移到 PostgreSQL）。
/// 只做三件事：列出视图、按视图分页、按视图筛选；口径始终在库里。
/// 元数据改用 information_schema（PostgreSQL），表名/列名用双引号包裹，值全部走 NpgsqlParameter。
/// </summary>
[ApiController]
[Route("api/views")]
[Authorize]
[资源("Rpt.Report")]
public class ViewsController : ControllerBase
{
    private readonly 数据服务 _data;

    public ViewsController(数据服务 数据) => _data = 数据;

    private static readonly (string 前缀, string 域)[] 域前缀 =
    {
        ("vw_采购", "采购"), ("vw_供应商", "采购"),
        ("vw_销售", "销售"), ("vw_客户", "销售"), ("vw_价格", "销售"),
        ("vw_配送", "配送"), ("vw_回场", "配送"), ("vw_车仓", "配送"),
        ("vw_钢瓶", "钢瓶"),
        ("vw_库存", "库存"), ("vw_五维", "库存"), ("vw_呆滞", "库存"),
        ("vw_凭证", "财务"), ("vw_科目", "财务"), ("vw_会计", "财务"), ("vw_试算", "财务"),
        ("vw_应收", "财务"), ("vw_应付", "财务"), ("vw_收付款", "财务"), ("vw_核销", "财务"),
        ("vw_费用", "财务"), ("vw_成本", "财务"), ("vw_期间", "财务"), ("vw_利润", "财务"),
        ("vw_资产负债", "财务"), ("vw_记账", "财务"), ("vw_付款计划", "财务"), ("vw_银行", "财务"),
        ("vw_提成", "人力"), ("vw_薪资", "人力"), ("vw_考勤", "人力"), ("vw_员工", "人力"),
        ("vw_接口", "集成"), ("vw_平台订单", "集成"), ("vw_电子秤", "集成"),
        ("vw_对账", "集成"), ("vw_支付", "集成"), ("vw_数据同步", "集成"), ("vw_消息", "集成"),
        ("vw_操作日志", "审计"), ("vw_登录", "审计"), ("vw_会话", "审计"),
        ("vw_数据留痕", "审计"), ("vw_数据导出", "审计"), ("vw_权限申请", "审计"),
        ("vw_离线队列", "移动"), ("vw_移动端", "移动"),
        ("vw_报表", "报表中心"), ("vw_驾驶舱", "报表中心"), ("vw_经营", "报表中心"),
        ("vw_毛利", "报表中心"), ("vw_商品", "报表中心"), ("vw_组织", "报表中心"),
        ("vw_车", "报表中心"), ("vw_仓库", "报表中心"), ("vw_字典", "报表中心"),
        ("vw_系统参数", "系统"), ("vw_参数", "系统"),
    };

    private static string 取域(string 视图)
    {
        foreach (var (前缀, 域) in 域前缀)
            if (视图.StartsWith(前缀, StringComparison.Ordinal)) return 域;
        return "其他";
    }

    /// <summary>视图清单（前端"报表中心"用它渲染目录）</summary>
    [HttpGet]
    [需要权限("VIEW")]
    public async Task<IActionResult> 清单(CancellationToken ct)
    {
        var rows = await _data.查询SqlAsync("""
            SELECT c.relname AS "视图",
                   (SELECT count(*) FROM information_schema.columns ic
                     WHERE ic.table_schema = 'public' AND ic.table_name = c.relname) AS "列数"
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind = 'v' AND c.relname LIKE 'vw\_%'
            ORDER BY c.relname
            """, ct);
        var 列表 = rows.Select(r => new
        {
            视图 = Convert.ToString(r["视图"]),
            域 = 取域(Convert.ToString(r["视图"]) ?? ""),
            列数 = Convert.ToInt32(r["列数"])
        }).ToList();
        return Ok(new
        {
            合计 = 列表.Count,
            分组 = 列表.GroupBy(x => x.域).OrderBy(g => g.Key)
                .Select(g => new { 域 = g.Key, 项 = g.ToList() })
        });
    }

    /// <summary>按视图分页取数。</summary>
    [HttpGet("{view}")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 数据(
        string view, CancellationToken ct,
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "size")] int size = 50,
        [FromQuery(Name = "keyword")] string? keyword = null,
        [FromQuery(Name = "from")] DateTime? from = null,
        [FromQuery(Name = "to")] DateTime? to = null,
        [FromQuery(Name = "orderBy")] string? orderBy = null,
        [FromQuery(Name = "desc")] bool desc = true)
    {
        if (!await 是合法视图(view, ct))
            return BadRequest(new { message = $"视图 {view} 不存在或不是 vw_ 开头的报表视图" });

        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 500);

        var 列 = await _data.查询SqlAsync("""
            SELECT ic.column_name AS "列", ic.data_type AS "型"
            FROM information_schema.columns ic
            WHERE ic.table_schema = 'public' AND ic.table_name = @v
            ORDER BY ic.ordinal_position
            """, ct, ("@v", view));

        if (列.Count == 0) return BadRequest(new { message = $"视图 {view} 没有可用列" });

        var 文本列 = 列.Where(c => c["型"] is "character varying" or "varchar" or "text" or "character" or "char")
                          .Select(c => Convert.ToString(c["列"])!).ToList();
        var 日期列 = 列.Where(c => c["型"] is "date" or "timestamp without time zone" or "timestamp with time zone" or "timestamp")
                          .Select(c => Convert.ToString(c["列"])!).ToList();
        var 全部列 = 列.Select(c => new { 列 = Convert.ToString(c["列"]), 型 = Convert.ToString(c["型"]) }).ToList();

        var where = new List<string>();
        var ps = new List<(string 名, object? 值)>();
        if (!string.IsNullOrWhiteSpace(keyword) && 文本列.Count > 0)
        {
            where.Add("(" + string.Join(" OR ", 文本列.Select(c => $"t.\"{c}\" LIKE @kw")) + ")");
            ps.Add(("@kw", $"%{keyword.Trim()}%"));
        }
        if (from is not null && 日期列.Count > 0)
        {
            where.Add($"t.\"{日期列[0]}\" >= @d1");
            ps.Add(("@d1", from.Value.Date));
        }
        if (to is not null && 日期列.Count > 0)
        {
            where.Add($"t.\"{日期列[0]}\" < @d2");
            ps.Add(("@d2", to.Value.Date.AddDays(1)));
        }
        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        var 排序列 = !string.IsNullOrWhiteSpace(orderBy) && 列.Any(c => Convert.ToString(c["列"]) == orderBy)
            ? orderBy!
            : 日期列.FirstOrDefault() ?? Convert.ToString(列[0]["列"])!;

        var total = await _data.标量Async<long>(
            $"SELECT COUNT(1) FROM public.\"{view}\" t {whereSql}", ct, ps.ToArray());

        var sql = $"""
            SELECT * FROM public."{view}" t
            {whereSql}
            ORDER BY t."{排序列}" {(desc ? "DESC" : "ASC")}
            OFFSET {(page - 1) * size} ROWS FETCH NEXT {size} ROWS ONLY
            """;
        var items = await _data.查询SqlAsync(sql, ct, ps.ToArray());

        return Ok(new { view, total, items, 列 = 全部列, 排序列, 日期列 = 日期列.FirstOrDefault() });
    }

    private async Task<bool> 是合法视图(string view, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(view) || view.Length > 128) return false;
        if (!view.StartsWith("vw_", StringComparison.Ordinal)) return false;
        var n = await _data.标量Async<int>(
            "SELECT COUNT(1) FROM information_schema.views WHERE table_schema = 'public' AND table_name = @v", ct, ("@v", view));
        return n > 0;
    }
}
