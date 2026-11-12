using GasInventory.Api.Auth;
using GasInventory.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GasInventory.Api.Controllers;

/// <summary>
/// 客户欠款与账龄（v3 往来域）。
///
/// 口径：应收余额/押金/借瓶全部取 vw_客户应收（已过账应收单聚合，与 vw_应收汇总 同口径），
/// 账龄明细取 vw_应收账龄 的「逾期天数」——未到期是负数、结清是 0，前端按 0-30/31-60/61-90/90+ 分档。
/// 不重算金额，避免与阶段 5 自检（应收应付账龄）出现两套数。
/// </summary>
[ApiController]
[Route("api/partners")]
[Authorize]
[资源("Fin.Receivable")]
public class PartnersReceivableController : ControllerBase
{
    private readonly 数据服务 _data;

    public PartnersReceivableController(数据服务 数据) => _data = 数据;

    /// <summary>客户欠款：只列应收余额 &gt; 0 的客户，按余额倒序分页。</summary>
    [HttpGet("customers")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 客户(
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "size")] int size = 50,
        [FromQuery(Name = "keyword")] string? keyword = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 500);

        var 条件 = new List<string> { "r.应收余额 > 0" };
        var ps = new List<(string 名, object? 值)>();
        var kw = keyword?.Trim();
        if (!string.IsNullOrEmpty(kw))
        {
            条件.Add("(k.客户编码 LIKE @kw OR k.客户简称 LIKE @kw OR k.联系电话 LIKE @kw)");
            ps.Add(("@kw", $"%{kw}%"));
        }
        var where = "WHERE " + string.Join(" AND ", 条件);

        var total = await _data.标量Async<long>($"""
            SELECT COUNT(1)
            FROM dbo.客户 k
            JOIN dbo.vw_客户应收 r ON r.客户编号 = k.客户编号
            {where}
            """, ct, ps.ToArray());

        var sql = $"""
            SELECT k.客户编号,
                   k.客户编码,
                   k.客户简称,
                   ISNULL(k.联系电话, '') AS 联系电话,
                   ISNULL(e.姓名, '')     AS 业务员,
                   ISNULL(q.区域名称, '')  AS 区域,
                   r.应收余额             AS 应收金额,
                   r.押金余额             AS 押金金额,
                   r.借出瓶数             AS 在借瓶数,
                   r.信用额度,
                   r.最后送货日期         AS 最后交易日期
            FROM dbo.客户 k
            JOIN dbo.vw_客户应收 r ON r.客户编号 = k.客户编号
            LEFT JOIN dbo.员工 e   ON e.员工编号 = k.业务员编号
            LEFT JOIN dbo.客户区域 q ON q.区域编号 = k.客户区域编号
            {where}
            ORDER BY r.应收余额 DESC, k.客户编码
            OFFSET {(page - 1) * size} ROWS FETCH NEXT {size} ROWS ONLY
            """;

        var items = await _data.查询SqlAsync(sql, ct, ps.ToArray());
        return Ok(new { total, items });
    }

    /// <summary>
    /// 单客户账龄：只列未收金额 &gt; 0 的已过账应收单。
    /// 账龄天数取「逾期天数」并下限截 0 —— 未到期/已结清都算 0，不把负数天数抛给前端。
    /// </summary>
    [HttpGet("ageing")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 账龄([FromQuery(Name = "客户编号")] int 客户编号, CancellationToken ct)
    {
        var 明细 = await _data.查询SqlAsync("""
            SELECT 单据号码,
                   单据日期  AS 送气日期,
                   应收金额  AS 金额合计,
                   已收金额,
                   未收金额,
                   CASE WHEN 逾期天数 > 0 THEN 逾期天数 ELSE 0 END AS 账龄天数
            FROM dbo.vw_应收账龄
            WHERE 客户编号 = @cid AND 单据状态 = 3 AND 未收金额 > 0
            ORDER BY 单据日期, 单据号码
            """, ct, ("@cid", 客户编号));

        // 合计取明细的同一批数，不另写聚合，保证与明细逐行相加一致
        var 合计 = new Dictionary<string, object?>();
        if (明细.Count > 0)
        {
            foreach (var 列 in new[] { "金额合计", "已收金额", "未收金额" })
                合计[列] = 明细.Sum(r => Convert.ToDecimal(r[列] ?? 0));
        }

        return Ok(new { 明细, 合计 });
    }
}

/// <summary>
/// 供应商应付。单独一个控制器只为权限资源不同（Fin.Payable）。
///
/// 字段名对齐前端 Suppliers.vue 的兜底键：供气商编码/供气商简称/联系电话/应付金额/已付金额/账期天数。
/// 「应付金额」取 **应付余额**（方向×应付−已付），不是应付单累计发生额——页面指标是"仍需付款"。
/// </summary>
[ApiController]
[Route("api/partners")]
[Authorize]
[资源("Fin.Payable")]
public class PartnersPayableController : ControllerBase
{
    private readonly 数据服务 _data;

    public PartnersPayableController(数据服务 数据) => _data = 数据;

    /// <summary>供应商应付一览（不分页，供应商数量级很小）。</summary>
    [HttpGet("suppliers")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 供应商(CancellationToken ct)
    {
        var rows = await _data.查询SqlAsync("""
            SELECT g.供应商编码             AS 供气商编码,
                   g.供应商简称             AS 供气商简称,
                   ISNULL(g.联系电话, '')   AS 联系电话,
                   ISNULL(s.应付余额, 0)    AS 应付金额,
                   ISNULL(s.已付金额, 0)    AS 已付金额,
                   g.结算天数               AS 账期天数,
                   (SELECT MAX(a.单据日期) FROM dbo.应付单 a
                    WHERE a.供应商编号 = g.供应商编号 AND a.单据状态 = 3) AS 最后交易日期
            FROM dbo.供应商 g
            LEFT JOIN dbo.vw_应付汇总 s ON s.供应商编号 = g.供应商编号
            ORDER BY ISNULL(s.应付余额, 0) DESC, g.供应商编码
            """, ct);
        return Ok(rows);
    }
}
