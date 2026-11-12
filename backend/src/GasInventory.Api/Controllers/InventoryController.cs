using GasInventory.Api.Auth;
using GasInventory.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GasInventory.Api.Controllers;

/// <summary>
/// 库存查询与对账（v3 库存域）。
///
/// 口径：全部直接读 v3 库存视图，不在 API 里重算数量/金额——
///   库存结存 ← vw_库存结存（仓库 × 商品 × 批次 × 状态 × 权属五维，由过账过程维护）
///   库存流水 ← vw_库存流水（只增不改的台账，正数入 / 负数出）
/// 自检 98 号「结存 = Σ流水」「结存金额 = Σ流水金额」保证两边同源。
/// </summary>
[ApiController]
[Route("api/inventory")]
[Authorize]
[资源("Inv.Stock")]
public class InventoryController : ControllerBase
{
    private readonly 数据服务 _data;

    public InventoryController(数据服务 数据) => _data = 数据;

    /// <summary>
    /// 库存查询：按 规格 × 仓库 汇总五维结存。
    ///
    /// v3 没有"空瓶/在借"这两个结存维度（空瓶是钢瓶资产状态，借出记在客户押金账户），
    /// 为了让前端「库存查询」的表结构不变，这两列固定返回 0；
    /// 报废取库存状态 SCRAP，实瓶取其余状态（AVAIL/INSPECT/FROZEN/TRANSIT 都是实物在账）。
    /// </summary>
    [HttpGet("stock")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 库存(CancellationToken ct)
    {
        const string sql = """
            SELECT 商品编号                  AS 规格编号,
                   商品名称                  AS 规格名称,
                   仓库名称                  AS 仓库,
                   SUM(CASE WHEN 库存状态代码 <> 'SCRAP' THEN 数量 ELSE 0 END) AS 实瓶数量,
                   CAST(0 AS int)            AS 空瓶数量,
                   CAST(0 AS int)            AS 在借瓶数,
                   SUM(CASE WHEN 库存状态代码 = 'SCRAP' THEN 数量 ELSE 0 END) AS 报废数量,
                   dbo.fn_round(SUM(成本金额), 2) AS 库存金额
            FROM dbo.vw_库存结存
            GROUP BY 商品编号, 商品名称, 仓库名称
            HAVING SUM(CASE WHEN 数量 <> 0 THEN 1 ELSE 0 END) > 0
            ORDER BY 库存金额 DESC, 规格名称, 仓库
            """;
        return Ok(await _data.查询SqlAsync(sql, ct));
    }

    /// <summary>
    /// 库存对账：某日「台账推算期末」与「五维结存账面」逐规格核对。
    ///
    /// 明细行只来自**当日已过账**的盘点单（盘点范围＝盘点单的仓库），
    /// 期初/入库/出库/借出/归还/期末都由库存流水按同一仓库范围汇总：
    ///   期初 = 该日之前 Σ(方向×数量)；期末 = 该日及之前 Σ(方向×数量)
    ///   入库 = 当日方向 1 的数量；出库 = 当日方向 -1 的数量（借出/归还分别是其子集，便于单看）
    /// 账面取库存结存相同仓库范围的 Σ数量，差异 = 盘点实盘 − 账面。
    /// 没有盘点单的日期返回空明细（前端显示"该日没有可对账的规格"），不拿账面硬凑盘点数。
    /// </summary>
    [HttpGet("reconcile")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 对账([FromQuery(Name = "日期")] DateTime? 日期, CancellationToken ct)
    {
        var 日 = (日期 ?? DateTime.Today).Date;

        const string sql = """
            WITH 盘点 AS (
                SELECT h.仓库编号, d.商品编号, SUM(d.实盘数量) AS 实盘
                FROM dbo.库存盘点单 h
                JOIN dbo.库存盘点单明细 d ON d.盘点单编号 = h.盘点单编号
                WHERE h.盘点日期 = @d AND h.单据状态 = 3 AND d.实盘数量 IS NOT NULL
                GROUP BY h.仓库编号, d.商品编号
            ),
            范围 AS (SELECT DISTINCT 商品编号, 仓库编号 FROM 盘点),
            盘点汇总 AS (SELECT 商品编号, SUM(实盘) AS 盘点实盘 FROM 盘点 GROUP BY 商品编号),
            流水 AS (
                SELECT l.商品编号,
                       SUM(CASE WHEN l.业务日期 <  @d THEN l.方向 * l.数量 ELSE 0 END) AS 期初,
                       SUM(CASE WHEN l.业务日期 =  @d AND l.方向 =  1 THEN l.数量 ELSE 0 END) AS 入库,
                       SUM(CASE WHEN l.业务日期 =  @d AND l.方向 = -1 THEN l.数量 ELSE 0 END) AS 出库,
                       SUM(CASE WHEN l.业务日期 =  @d AND l.业务类型代码 = 'BORROW_OUT' THEN l.数量 ELSE 0 END) AS 借出,
                       SUM(CASE WHEN l.业务日期 =  @d AND l.业务类型代码 = 'BORROW_IN'  THEN l.数量 ELSE 0 END) AS 归还,
                       SUM(CASE WHEN l.业务日期 <= @d THEN l.方向 * l.数量 ELSE 0 END) AS 期末
                FROM dbo.vw_库存流水 l
                WHERE l.业务日期 <= @d
                  AND EXISTS (SELECT 1 FROM 范围 s WHERE s.商品编号 = l.商品编号 AND s.仓库编号 = l.仓库编号)
                GROUP BY l.商品编号
            ),
            账面 AS (
                SELECT b.商品编号, SUM(b.数量) AS 账面
                FROM dbo.vw_库存结存 b
                WHERE EXISTS (SELECT 1 FROM 范围 s WHERE s.商品编号 = b.商品编号 AND s.仓库编号 = b.仓库编号)
                GROUP BY b.商品编号
            )
            SELECT p.商品名称 AS 规格名称,
                   ISNULL(f.期初, 0)     AS 期初,
                   ISNULL(f.入库, 0)     AS 入库,
                   ISNULL(f.出库, 0)     AS 出库,
                   ISNULL(f.借出, 0)     AS 借出,
                   ISNULL(f.归还, 0)     AS 归还,
                   ISNULL(f.期末, 0)     AS 期末,
                   ISNULL(a.账面, 0)     AS 账面,
                   ISNULL(k.盘点实盘, 0) AS 盘点实盘,
                   ISNULL(k.盘点实盘, 0) - ISNULL(a.账面, 0) AS 差异
            FROM 盘点汇总 k
            JOIN dbo.商品 p ON p.商品编号 = k.商品编号
            LEFT JOIN 流水 f ON f.商品编号 = k.商品编号
            LEFT JOIN 账面 a ON a.商品编号 = k.商品编号
            ORDER BY p.商品名称
            """;

        var 明细 = await _data.查询SqlAsync(sql, ct, ("@d", 日));

        // 合计只在真有明细时返回：空数组配 0 合计会让人误以为"盘点过、账实相符"
        var 合计 = new Dictionary<string, object?>();
        if (明细.Count > 0)
        {
            foreach (var 列 in new[] { "期初", "入库", "出库", "借出", "归还", "期末", "账面", "盘点实盘", "差异" })
                合计[列] = 明细.Sum(r => Convert.ToInt64(r[列] ?? 0));
        }

        return Ok(new { 明细, 合计 });
    }
}

/// <summary>
/// 出入库流水（库存台账）。单独一个控制器只是因为权限资源不同（Inv.Ledger），
/// 路由仍在 api/inventory 下，前端契约不变。
/// </summary>
[ApiController]
[Route("api/inventory")]
[Authorize]
[资源("Inv.Ledger")]
public class InventoryLedgerController : ControllerBase
{
    private readonly 数据服务 _data;

    public InventoryLedgerController(数据服务 数据) => _data = 数据;

    /// <summary>
    /// 分页查流水：page/size + 可选 规格编号（=商品编号）与业务日期区间（含首尾）。
    /// 单据号码按单据类型回查各自单据表（不走 出入库单据池：池里的 单据号码 列当前为空）。
    /// </summary>
    [HttpGet("ledger")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 流水(
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "size")] int size = 50,
        [FromQuery(Name = "规格编号")] int? 规格编号 = null,
        [FromQuery(Name = "from")] DateTime? from = null,
        [FromQuery(Name = "to")] DateTime? to = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 500);

        var 条件 = new List<string>();
        var ps = new List<(string 名, object? 值)>();
        if (规格编号 is > 0)
        {
            条件.Add("l.商品编号 = @pid");
            ps.Add(("@pid", 规格编号));
        }
        if (from is DateTime 起)
        {
            条件.Add("l.业务日期 >= @from");
            ps.Add(("@from", 起.Date));
        }
        if (to is DateTime 止)
        {
            条件.Add("l.业务日期 <= @to");
            ps.Add(("@to", 止.Date));
        }
        var where = 条件.Count == 0 ? "" : "WHERE " + string.Join(" AND ", 条件);

        var total = await _data.标量Async<long>($"SELECT COUNT(1) FROM dbo.vw_库存流水 l {where}", ct, ps.ToArray());

        var sql = $"""
            SELECT l.业务日期,
                   l.商品名称                AS 规格名称,
                   l.业务类型名称,
                   l.净数量                  AS 实瓶变动,
                   CAST(0 AS int)            AS 空瓶变动,
                   l.单据类型名称            AS 单据类型,
                   COALESCE(s.单据号码, p.单据号码, t.单据号码, k.单据号码, j.单据号码, '') AS 单据号码,
                   ISNULL(l.客户简称, '')    AS 客户,
                   ISNULL(l.供应商简称, '')  AS 供气商,
                   ISNULL(l.备注, '')        AS 备注
            FROM dbo.vw_库存流水 l
            LEFT JOIN dbo.送气单 s     ON l.单据类型代码 = 'SALES'     AND s.送气单编号 = l.单据编号
            LEFT JOIN dbo.到货单 p     ON l.单据类型代码 = 'PURCHASE'  AND p.到货单编号 = l.单据编号
            LEFT JOIN dbo.库存调拨单 t ON l.单据类型代码 = 'TRANSFER'  AND t.调拨单编号 = l.单据编号
            LEFT JOIN dbo.库存盘点单 k ON l.单据类型代码 = 'STOCKTAKE' AND k.盘点单编号 = l.单据编号
            LEFT JOIN dbo.库存调整单 j ON l.单据类型代码 = 'ADJUST'    AND j.调整单编号 = l.单据编号
            {where}
            ORDER BY l.业务日期 DESC, l.流水编号 DESC
            OFFSET {(page - 1) * size} ROWS FETCH NEXT {size} ROWS ONLY
            """;

        var items = await _data.查询SqlAsync(sql, ct, ps.ToArray());
        return Ok(new { total, items });
    }
}
