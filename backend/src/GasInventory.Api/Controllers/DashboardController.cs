using GasInventory.Api.Auth;
using GasInventory.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GasInventory.Api.Controllers;

/// <summary>
/// 首页看板。
///
/// **批次 7 重写**：原实现整块是 v2 口径（送气日期 / 金额合计 / 销量合计 / 进气单主干 /
/// 借瓶单明细 / 客户.是否有效），而 v3 已换模型 —— 4 条 SQL 里有 3 条列名或表名不存在，
/// 整个接口 500，首页整页空白。现在一律读 v3 真实列与 v3 视图：
///   送气单{送货日期, 销售金额, 销售数量, 是否已过账}
///   到货单 + 到货单明细（v3 没有 进气单主干）
///   vw_库存结存 / vw_客户复购 / vw_应收汇总 / vw_应收账龄
///
/// 锚点：演示数据的最新送货日期是 2026-09-30，不是"今天"。所以「截止」取
/// MAX(送货日期)，今日/本月/趋势都以它为基准 —— 否则今日 0 单、本月 0 元，整页空白。
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly 数据服务 _data;

    public DashboardController(数据服务 data) => _data = data;

    [HttpGet]
    [资源("Rpt.Dashboard")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 概览(CancellationToken ct)
    {
        var 截止 = (await _data.标量Async<DateTime?>("SELECT MAX(送货日期) FROM dbo.送气单", ct))?.Date
                   ?? DateTime.Today;
        var 月首 = new DateTime(截止.Year, 截止.Month, 1);
        var 次月首 = 月首.AddMonths(1);

        // 今日 / 本月：送气单按 送货日期 + 是否已过账 = 1（未过账的单不进经营口径）
        var 今日 = (await _data.查询SqlAsync("""
            SELECT COUNT(1)                    AS 单数,
                   ISNULL(SUM(销售金额), 0)    AS 金额,
                   ISNULL(SUM(销售数量), 0)    AS 销量
            FROM dbo.送气单
            WHERE 是否已过账 = 1
              AND 送货日期 >= @d AND 送货日期 < DATEADD(day, 1, @d)
            """, ct, ("@d", 截止))).FirstOrDefault() ?? new();

        var 本月 = (await _data.查询SqlAsync("""
            SELECT COUNT(1)                    AS 单数,
                   ISNULL(SUM(销售金额), 0)    AS 金额,
                   ISNULL(SUM(销售数量), 0)    AS 销量
            FROM dbo.送气单
            WHERE 是否已过账 = 1
              AND 送货日期 >= @m AND 送货日期 < @n
            """, ct, ("@m", 月首), ("@n", 次月首))).FirstOrDefault() ?? new();

        // 进气：v3 没有「进气单主干」，采购入库是 到货单（404 行，全部 单据状态 = 3 已过账）
        // 单数必须 COUNT(DISTINCT 单号) —— 连了明细，COUNT(1) 会变成行数
        var 进气 = (await _data.查询SqlAsync("""
            SELECT COUNT(DISTINCT h.到货单编号) AS 单数,
                   ISNULL(SUM(ISNULL(d.不含税金额, 0) + ISNULL(d.税额, 0)), 0) AS 金额
            FROM dbo.到货单 h
            JOIN dbo.到货单明细 d ON d.到货单编号 = h.到货单编号
            WHERE h.单据状态 = 3
              AND h.到货日期 >= @m AND h.到货日期 < @n
            """, ct, ("@m", 月首), ("@n", 次月首))).FirstOrDefault() ?? new();

        // 库存：实瓶 = 非 SCRAP 的 Σ数量，报废单独看 SCRAP（与批次 6 第 3 步 / 库存查询同一口径）。
        // v3 没有"空瓶 / 在借"这两个结存维度（空瓶是钢瓶资产状态，借出记在 客户押金账户），
        // 如实返回 0，不拿别处数字硬凑。
        var 库存 = (await _data.查询SqlAsync("""
            SELECT ISNULL(SUM(CASE WHEN 库存状态代码 <> 'SCRAP' THEN 数量 ELSE 0 END), 0) AS 实瓶,
                   CAST(0 AS int) AS 空瓶,
                   CAST(0 AS int) AS 在借,
                   ISNULL(dbo.fn_round(SUM(成本金额), 2), 0) AS 库存金额
            FROM dbo.vw_库存结存
            """, ct)).FirstOrDefault() ?? new();

        // 客户：总数取 客户.是否有效 = 1；活跃数 = 近 90 天有送货（vw_客户复购.最后送气日期）
        var 客户 = (await _data.查询SqlAsync("""
            SELECT (SELECT COUNT(1) FROM dbo.客户 WHERE 是否有效 = 1) AS 总数,
                   (SELECT COUNT(1) FROM dbo.vw_客户复购
                     WHERE 最后送气日期 >= DATEADD(day, -90, @d)) AS 活跃数
            """, ct, ("@d", 截止))).FirstOrDefault() ?? new();

        // 应收：总额取 应收汇总.应收余额；逾期 90 天以上取 应收账龄.未收金额（逾期天数 >= 90）
        var 应收 = (await _data.查询SqlAsync("""
            SELECT (SELECT ISNULL(SUM(应收余额), 0) FROM dbo.vw_应收汇总) AS 总额,
                   (SELECT ISNULL(SUM(未收金额), 0) FROM dbo.vw_应收账龄
                     WHERE 逾期天数 >= 90) AS 逾期90天
            """, ct)).FirstOrDefault() ?? new();

        // 趋势：以 截止 为终点的最近 14 天
        var 趋势 = await _data.查询SqlAsync("""
            SELECT CONVERT(char(10), 送货日期, 23) AS 日期,
                   COUNT(1)                        AS 单数,
                   ISNULL(SUM(销售金额), 0)        AS 金额
            FROM dbo.送气单
            WHERE 是否已过账 = 1
              AND 送货日期 >= DATEADD(day, -13, @d)
              AND 送货日期 <  DATEADD(day, 1, @d)
            GROUP BY CONVERT(char(10), 送货日期, 23)
            ORDER BY 日期
            """, ct, ("@d", 截止));

        // 规格占比：v3 结存按商品分组（非 SCRAP）
        var 规格占比 = await _data.查询SqlAsync("""
            SELECT 商品名称 AS 规格, ISNULL(SUM(数量), 0) AS 数量
            FROM dbo.vw_库存结存
            WHERE 库存状态代码 <> 'SCRAP'
            GROUP BY 商品名称
            HAVING SUM(数量) > 0
            ORDER BY 数量 DESC
            """, ct);

        return Ok(new { 截止, 今日, 本月, 进气, 库存, 客户, 应收, 趋势, 规格占比 });
    }
}
