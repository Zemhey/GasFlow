using GasInventory.Api.Auth;
using GasInventory.Api.Services;
using GasInventory.Infrastructure.Data;
using GasInventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace GasInventory.Api.Controllers;

/// <summary>
/// 库存作业（批次 19）：调拨 / 调整 / 冻结。
///
/// 三条口径（都以库为准，页面不另算账）：
///   · 数量账只能由 usp_库存_* 过程改，**绝不 UPDATE 库存结存**（结存是流水的镜像，由 trg_库存流水_结存 维护）；
///   · 调拨/调整/冻结都先成单再调过程：单据没有建单过程（与盘点单同一情形），
///     所以主表+明细按 56 号配送过程的写法直插，**过账一律走过程**；
///   · 「是否经过在途」由**单据列**决定，不是系统参数（库里 biz.* / inv.* 都没有这个开关，实测已核）：
///     = 1 时调入仓先进 TRANSIT 并登记 调拨在途，到达时由 usp_库存_到达在途 转 AVAIL；= 0 时一步到位。
///
/// 与批次 18 的衔接：调拨是**数量口径**的仓间转移，钢瓶移动是**一物一码口径**，两套账互不搬。
/// usp_库存_调拨 里那段「给带钢瓶编号的流水补写 钢瓶流转明细」在当前调用下不会触发
/// （出/入都没传 @钢瓶编号，恒为 NULL），所以要搬瓶仍需单独走 usp_钢瓶_移动。
/// </summary>
[ApiController]
[Route("api/inventory/ops")]
[Authorize]
[资源("Inv.Transfer")]
public class StockOpsController : ControllerBase
{
    /// <summary>可作为调拨/调整/冻结来源的库存状态。TRANSIT 由到达过程消化，SCRAP 是终态。</summary>
    private static readonly string[] 可作业状态 = { "AVAIL", "FROZEN", "INSPECT" };
    private static readonly string[] 权属集合 = { "OWN", "CUST", "SUPP" };
    private static readonly string[] 调整类型集合 = { "盘盈", "盘亏", "报损", "赠品", "样品", "其他" };
    private static readonly string[] 冻结来源集合 = { "质检", "纠纷", "盘点", "其他" };

    private readonly 数据服务 _data;
    private readonly 当前登录 _me;
    private readonly GasInventoryDbContext _db;

    public StockOpsController(数据服务 data, 当前登录 me, GasInventoryDbContext db)
    {
        _data = data;
        _me = me;
        _db = db;
    }

    /// <summary>
    /// 并行批次会各自起副本 API，探针/页面据此确认这个端口连的是哪个库
    /// （与 /api/cylinders/db 同一用途：先比库名再写，否则会把作业落进别人的副本）。
    /// </summary>
    [HttpGet("db")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 库名(CancellationToken ct)
    {
        var r = await _data.查询SqlAsync("""
            SELECT DB_NAME() AS 库, @@SERVICENAME AS 实例,
                   (SELECT COUNT(1) FROM dbo.库存结存) AS 结存行数,
                   (SELECT ISNULL(SUM(数量), 0) FROM dbo.库存结存) AS 结存数量,
                   (SELECT COUNT(1) FROM dbo.库存流水) AS 流水行数
            """, ct);
        return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?> { ["库"] = null });
    }

    // ================================================================ 结存（可用/冻结/合计）

    /// <summary>
    /// 五维结存列表：按「仓库 × 商品 × 批次 × 权属」成行，**库存状态转成列**
    /// （可用 / 冻结 / 在途 / 待检 / 合计），这就是冻结作业要挑的那张表。
    /// 可用 = Σ(状态 AVAIL 的数量 − 锁定量)，合计 = Σ全部状态数量，账不平时无处可藏。
    /// </summary>
    [HttpGet("balance")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 结存(
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "size")] int size = 50,
        [FromQuery(Name = "keyword")] string? keyword = null,
        [FromQuery(Name = "仓库编号")] int? 仓库编号 = null,
        [FromQuery(Name = "商品编号")] int? 商品编号 = null,
        [FromQuery(Name = "只看有量")] bool? 只看有量 = null,
        [FromQuery(Name = "只看可冻结")] bool? 只看可冻结 = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 500);

        var 条件 = new List<string>();
        var ps = new List<(string 名, object? 值)>();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            条件.Add("""
                (p.商品名称 LIKE @kw OR p.商品编码 LIKE @kw OR bt.批次号 LIKE @kw
                 OR w.仓库名称 LIKE @kw OR ISNULL(n.网点名称, '') LIKE @kw)
                """);
            ps.Add(("@kw", $"%{keyword.Trim()}%"));
        }
        if (仓库编号 is > 0)
        {
            条件.Add("s.仓库编号 = @wh");
            ps.Add(("@wh", 仓库编号.Value));
        }
        if (商品编号 is > 0)
        {
            条件.Add("s.商品编号 = @pid");
            ps.Add(("@pid", 商品编号.Value));
        }
        var where = 条件.Count == 0 ? "" : "WHERE " + string.Join(" AND ", 条件);

        // 有量/可冻结是对**聚合后**的过滤，必须放 HAVING，不能混进 WHERE
        var having = new List<string>();
        if (只看有量 == true) having.Add("SUM(s.[数量]) <> 0");
        if (只看可冻结 == true)
            having.Add("SUM(CASE WHEN s.[库存状态代码] = 'AVAIL' THEN s.[可用数量] ELSE 0 END) > 0");
        var havingSql = having.Count == 0 ? "" : "HAVING " + string.Join(" AND ", having);

        var cte = $"""
            ;WITH x AS (
                SELECT s.[仓库编号], w.[仓库名称], w.[仓库类型], w.[是否车仓], w.[所属网点编号],
                       ISNULL(n.[网点名称], '') AS 网点名称,
                       s.[商品编号], p.[商品编码], p.[商品名称], p.[计量单位],
                       s.[批次编号], bt.[批次号], bt.[生产日期], bt.[到期日期], bt.[剩余数量],
                       s.[权属代码], q.[权属名称],
                       SUM(CASE WHEN s.[库存状态代码] = 'AVAIL' THEN s.[数量] ELSE 0 END) AS 可用结存,
                       SUM(CASE WHEN s.[库存状态代码] = 'AVAIL' THEN s.[锁定量] ELSE 0 END) AS 锁定量,
                       SUM(CASE WHEN s.[库存状态代码] = 'AVAIL' THEN s.[可用数量] ELSE 0 END) AS 可用,
                       SUM(CASE WHEN s.[库存状态代码] = 'FROZEN' THEN s.[数量] ELSE 0 END) AS 冻结,
                       SUM(CASE WHEN s.[库存状态代码] = 'TRANSIT' THEN s.[数量] ELSE 0 END) AS 在途,
                       SUM(CASE WHEN s.[库存状态代码] = 'INSPECT' THEN s.[数量] ELSE 0 END) AS 待检,
                       SUM(CASE WHEN s.[库存状态代码] = 'SCRAP' THEN s.[数量] ELSE 0 END) AS 报废,
                       SUM(s.[数量]) AS 合计,
                       dbo.fn_round(SUM(s.[成本金额]), 2) AS 成本金额,
                       ISNULL(MAX(CASE WHEN s.[库存状态代码] = 'AVAIL' THEN s.[成本单价] END),
                              MAX(s.[成本单价])) AS 成本单价
                FROM dbo.[库存结存] s
                JOIN dbo.[仓库] w ON w.[仓库编号] = s.[仓库编号]
                JOIN dbo.[商品] p ON p.[商品编号] = s.[商品编号]
                JOIN dbo.[库存批次] bt ON bt.[批次编号] = s.[批次编号]
                JOIN dbo.[库存权属] q ON q.[权属代码] = s.[权属代码]
                LEFT JOIN dbo.[经营网点] n ON n.[网点编号] = w.[所属网点编号]
                {where}
                GROUP BY s.[仓库编号], w.[仓库名称], w.[仓库类型], w.[是否车仓], w.[所属网点编号],
                         n.[网点名称], s.[商品编号], p.[商品编码], p.[商品名称], p.[计量单位],
                         s.[批次编号], bt.[批次号], bt.[生产日期], bt.[到期日期], bt.[剩余数量],
                         s.[权属代码], q.[权属名称]
                {havingSql}
            )
            """;

        var total = await _data.标量Async<long>($"{cte} SELECT COUNT(1) FROM x;", ct, ps.ToArray());
        var items = await _data.查询SqlAsync($"""
            {cte}
            SELECT * FROM x ORDER BY 合计 DESC, 仓库编号, 商品编号, 批次编号
             OFFSET {(page - 1) * size} ROWS FETCH NEXT {size} ROWS ONLY;
            """, ct, ps.ToArray());
        var 合计行 = (await _data.查询SqlAsync($"""
            {cte}
            SELECT CAST(ISNULL(SUM(可用), 0) AS varchar(20)) AS 可用,
                   CAST(ISNULL(SUM(冻结), 0) AS varchar(20)) AS 冻结,
                   CAST(ISNULL(SUM(在途), 0) AS varchar(20)) AS 在途,
                   CAST(ISNULL(SUM(合计), 0) AS varchar(20)) AS 合计,
                   CAST(ISNULL(dbo.fn_round(SUM(成本金额), 2), 0) AS varchar(30)) AS 成本金额
            FROM x;
            """, ct, ps.ToArray())).FirstOrDefault() ?? new Dictionary<string, object?>();

        return Ok(new { total, items, 合计行 });
    }

    // ================================================================ 调拨

    /// <summary>调拨单列表：一单一行，带行数/总量/在途行数与两个仓库名。</summary>
    [HttpGet("transfers")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 调拨列表(
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "size")] int size = 20,
        [FromQuery(Name = "keyword")] string? keyword = null,
        [FromQuery(Name = "单据状态")] int? 单据状态 = null,
        [FromQuery(Name = "在途状态")] string? 在途状态 = null,
        [FromQuery(Name = "from")] DateTime? from = null,
        [FromQuery(Name = "to")] DateTime? to = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);

        var 条件 = new List<string>();
        var ps = new List<(string 名, object? 值)>();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            条件.Add("""
                (t.单据号码 LIKE @kw OR w1.仓库名称 LIKE @kw OR w2.仓库名称 LIKE @kw
                 OR ISNULL(n.网点名称, '') LIKE @kw)
                """);
            ps.Add(("@kw", $"%{keyword.Trim()}%"));
        }
        if (单据状态 is not null)
        {
            条件.Add("t.单据状态 = @st");
            ps.Add(("@st", 单据状态.Value));
        }
        if (!string.IsNullOrWhiteSpace(在途状态))
        {
            条件.Add("t.在途状态 = @ts");
            ps.Add(("@ts", 在途状态.Trim()));
        }
        if (from is DateTime 起)
        {
            条件.Add("t.调拨日期 >= @from");
            ps.Add(("@from", 起.Date));
        }
        if (to is DateTime 止)
        {
            条件.Add("t.调拨日期 <= @to");
            ps.Add(("@to", 止.Date));
        }
        var where = 条件.Count == 0 ? "" : "WHERE " + string.Join(" AND ", 条件);

        var cte = """
            ;WITH x AS (
                SELECT t.[调拨单编号], t.[单据号码], t.[调拨日期], t.[调拨类型],
                       t.[是否跨网点], t.[是否经过在途], t.[在途状态], t.[发出时间],
                       t.[预计到达时间], t.[实际到达时间], t.[单据状态], t.[备注],
                       t.[调出仓库编号], w1.[仓库名称] AS 调出仓库, t.[调入仓库编号], w2.[仓库名称] AS 调入仓库,
                       ISNULL(n.[网点名称], '') AS 网点名称, ISNULL(e.[姓名], '') AS 经办人,
                       ISNULL((SELECT COUNT(1) FROM dbo.[库存调拨单明细] d
                                WHERE d.[调拨单编号] = t.[调拨单编号]), 0) AS 行数,
                       ISNULL((SELECT SUM(d.[调拨数量]) FROM dbo.[库存调拨单明细] d
                                WHERE d.[调拨单编号] = t.[调拨单编号]), 0) AS 调拨总量,
                       ISNULL((SELECT SUM(d.[实到数量]) FROM dbo.[库存调拨单明细] d
                                WHERE d.[调拨单编号] = t.[调拨单编号]), 0) AS 实到总量,
                       ISNULL((SELECT COUNT(1) FROM dbo.[调拨在途] a
                                WHERE a.[调拨单编号] = t.[调拨单编号] AND a.[状态] = N'在途'), 0) AS 在途行数,
                       CASE t.[单据状态] WHEN 0 THEN N'草稿' WHEN 1 THEN N'已提交' WHEN 2 THEN N'已审核'
                            WHEN 3 THEN N'已过账' WHEN 9 THEN N'已作废' ELSE N'其他' END AS 状态名称
                FROM dbo.[库存调拨单] t
                LEFT JOIN dbo.[仓库] w1 ON w1.[仓库编号] = t.[调出仓库编号]
                LEFT JOIN dbo.[仓库] w2 ON w2.[仓库编号] = t.[调入仓库编号]
                LEFT JOIN dbo.[经营网点] n ON n.[网点编号] = w1.[所属网点编号]
                LEFT JOIN dbo.[员工] e ON e.[员工编号] = t.[经办人编号]
            )
            """;

        var total = await _data.标量Async<long>($"{cte} SELECT COUNT(1) FROM x {where};", ct, ps.ToArray());
        var items = await _data.查询SqlAsync($"""
            {cte}
            SELECT * FROM x {where} ORDER BY 调拨单编号 DESC
             OFFSET {(page - 1) * size} ROWS FETCH NEXT {size} ROWS ONLY;
            """, ct, ps.ToArray());
        return Ok(new { total, items });
    }

    /// <summary>调拨单详情：主表 + 明细（含批次/商品名）+ 在途行。</summary>
    [HttpGet("transfers/{id:int}")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 调拨详情(int id, CancellationToken ct)
    {
        var 主表 = (await _data.查询SqlAsync("""
            SELECT t.*, w1.仓库名称 AS 调出仓库, w2.仓库名称 AS 调入仓库,
                   ISNULL(e.姓名, '') AS 经办人, v.车牌 AS 关联车牌
              FROM dbo.库存调拨单 t
              LEFT JOIN dbo.仓库 w1 ON w1.仓库编号 = t.调出仓库编号
              LEFT JOIN dbo.仓库 w2 ON w2.仓库编号 = t.调入仓库编号
              LEFT JOIN dbo.员工 e ON e.员工编号 = t.经办人编号
              LEFT JOIN dbo.车辆 v ON v.车辆编号 = t.关联车辆编号
             WHERE t.调拨单编号 = @id;
            """, ct, ("@id", id))).FirstOrDefault();
        if (主表 is null) return NotFound(new { message = "调拨单不存在" });

        var 明细 = await _data.查询SqlAsync("""
            SELECT d.明细编号, d.行号, d.商品编号, p.商品编码, p.商品名称,
                   d.批次编号, b.批次号, b.生产日期, b.到期日期,
                   d.库存状态代码, z.状态名称, d.权属代码, q.权属名称,
                   d.调拨数量, d.实到数量, d.差异数量, d.成本单价, d.行金额, d.备注
              FROM dbo.库存调拨单明细 d
              JOIN dbo.商品 p ON p.商品编号 = d.商品编号
              JOIN dbo.库存批次 b ON b.批次编号 = d.批次编号
              JOIN dbo.库存状态 z ON z.状态代码 = d.库存状态代码
              JOIN dbo.库存权属 q ON q.权属代码 = d.权属代码
             WHERE d.调拨单编号 = @id ORDER BY d.行号;
            """, ct, ("@id", id));

        var 在途 = await _data.查询SqlAsync("""
            SELECT a.在途编号, a.明细编号, a.商品编号, p.商品名称, a.批次编号, b.批次号,
                   a.数量, a.发出仓库编号, a.调入仓库编号, a.发出时间, a.实到时间, a.状态
              FROM dbo.调拨在途 a
              JOIN dbo.商品 p ON p.商品编号 = a.商品编号
              JOIN dbo.库存批次 b ON b.批次编号 = a.批次编号
             WHERE a.调拨单编号 = @id ORDER BY a.在途编号;
            """, ct, ("@id", id));

        return Ok(new { 主表, 明细, 在途 });
    }

    public sealed class 调拨明细行
    {
        public int 商品编号 { get; set; }
        public int 批次编号 { get; set; }
        public int 调拨数量 { get; set; }
        public string? 库存状态代码 { get; set; }
        public string? 权属代码 { get; set; }
        public decimal? 成本单价 { get; set; }
        public string? 备注 { get; set; }
    }

    public sealed class 调拨入参
    {
        public int 调出仓库编号 { get; set; }
        public int 调入仓库编号 { get; set; }
        public DateTime? 调拨日期 { get; set; }
        /// <summary>不填则按"涉及车仓必须走在途、其余一步到位"推导；跨网点不自动等于在途（库里没有这个开关）。</summary>
        public bool? 是否经过在途 { get; set; }
        public int? 关联车辆编号 { get; set; }
        public string? 备注 { get; set; }
        public bool 过账 { get; set; }
        public List<调拨明细行>? 明细 { get; set; }
    }

    /// <summary>建调拨单（可选同批过账）。过账 = usp_库存_调拨，一行明细两条流水（出调出仓 + 入调入仓）。</summary>
    [HttpPost("transfer")]
    [需要权限("ADD")]
    public async Task<IActionResult> 建调拨单([FromBody] 调拨入参 body, CancellationToken ct)
    {
        var 明细 = (body.明细 ?? new List<调拨明细行>())
            .Where(x => x.调拨数量 != 0)
            .ToList();
        if (明细.Count == 0) return BadRequest(new { message = "请至少填写一行调拨明细（数量大于 0）" });
        if (明细.Count > 200) return BadRequest(new { message = "单张调拨单最多 200 行，请拆单" });
        if (body.调出仓库编号 <= 0 || body.调入仓库编号 <= 0)
            return BadRequest(new { message = "请选择调出仓库和调入仓库" });
        if (body.调出仓库编号 == body.调入仓库编号)
            return BadRequest(new { message = "调出仓与调入仓相同，无需调拨（CK_库存调拨单_不同仓）" });

        var 日期 = body.调拨日期?.Date ?? DateTime.Today;
        var 仓 = await _data.查询SqlAsync("""
            SELECT w.仓库编号, w.仓库名称, w.是否有效, w.是否车仓, w.所属网点编号, ISNULL(n.网点名称, '') AS 网点名称
              FROM dbo.仓库 w LEFT JOIN dbo.经营网点 n ON n.网点编号 = w.所属网点编号
             WHERE w.仓库编号 IN (@out, @in);
            """, ct, ("@out", body.调出仓库编号), ("@in", body.调入仓库编号));
        var 调出 = 仓.FirstOrDefault(x => 整数(x, "仓库编号") == body.调出仓库编号);
        var 调入 = 仓.FirstOrDefault(x => 整数(x, "仓库编号") == body.调入仓库编号);
        if (调出 is null || 调入 is null) return BadRequest(new { message = "调出仓或调入仓不存在" });
        if (!布尔(调出, "是否有效") || !布尔(调入, "是否有效"))
            return BadRequest(new { message = "仓库已停用，不能作为调拨两端" });

        var 涉及车仓 = 布尔(调出, "是否车仓") || 布尔(调入, "是否车仓");
        var 跨网点 = 整数(调出, "所属网点编号") != 整数(调入, "所属网点编号");
        var 经过在途 = body.是否经过在途 ?? 涉及车仓;
        if (涉及车仓 && 经过在途 == false)
            return BadRequest(new { message = "车仓两端必须走在途（装车/回场口径：车仓只能持有 TRANSIT 数量）" });

        // 五维与可用量：先探一次，给人话错误信息；真拦截仍由 usp_库存_出 在锁内做
        var 行键 = new List<(int 商品, int 批次, string 状态, string 权属, int 数量, decimal? 成本, string? 备注)>();
        foreach (var x in 明细)
        {
            var 状态 = (x.库存状态代码 ?? "AVAIL").Trim().ToUpperInvariant();
            var 权属 = (x.权属代码 ?? "OWN").Trim().ToUpperInvariant();
            if (x.商品编号 <= 0 || x.批次编号 <= 0)
                return BadRequest(new { message = "每行都必须选商品与批次" });
            if (x.调拨数量 <= 0)
                return BadRequest(new { message = "调拨数量必须大于 0（减少数量请用调整单）" });
            if (!可作业状态.Contains(状态))
                return BadRequest(new { message = $"库存状态 {状态} 不能调拨（可用：AVAIL / FROZEN / INSPECT；在途由到达过程消化）" });
            if (!权属集合.Contains(权属))
                return BadRequest(new { message = $"权属 {权属} 不存在" });
            if (经过在途 && 权属 != "OWN")
                return BadRequest(new { message = "走在途的调拨目前只能自有权属：usp_库存_到达在途 把到达后的权属写死为 OWN" });
            行键.Add((x.商品编号, x.批次编号, 状态, 权属, x.调拨数量, x.成本单价, x.备注));
        }

        var ps = new List<(string, object?)> { ("@out", body.调出仓库编号) };
        var vals = new List<string>();
        for (var i = 0; i < 行键.Count; i++)
        {
            ps.Add(($"@p{i}", 行键[i].商品));
            ps.Add(($"@b{i}", 行键[i].批次));
            vals.Add($"(@p{i}, @b{i})");
        }
        // 批次 × 商品 的候选集用 VALUES 派生表：`WHERE 列 IN ((a,b),(c,d))` 是语法错误
        var 现量 = await _data.查询SqlAsync($"""
            SELECT b.[批次编号], b.[商品编号], b.[批次号], b.[是否有效], p.[商品名称],
                   ISNULL(s.[库存状态代码], '') AS [状态], ISNULL(s.[权属代码], '') AS [权属],
                   ISNULL(s.[数量], 0) AS [数量], ISNULL(s.[可用数量], 0) AS [可用数量],
                   ISNULL(s.[成本单价], 0) AS [成本单价]
            FROM (VALUES {string.Join(",", vals)}) v([商品编号], [批次编号])
            JOIN dbo.[库存批次] b ON b.[批次编号] = v.[批次编号]
            JOIN dbo.[商品] p ON p.[商品编号] = b.[商品编号]
            LEFT JOIN dbo.[库存结存] s ON s.[批次编号] = b.[批次编号]
                                      AND s.[商品编号] = b.[商品编号] AND s.[仓库编号] = @out
            """, ct, ps.ToArray());

        var 需求 = new Dictionary<string, int>();
        foreach (var x in 行键)
        {
            var 批 = 现量.FirstOrDefault(r => 整数(r, "批次编号") == x.批次);
            if (批 is null) return BadRequest(new { message = $"批次 {x.批次} 不存在" });
            if (整数(批, "商品编号") != x.商品)
                return BadRequest(new { message = $"批次 {字符串(批, "批次号")} 属于「{字符串(批, "商品名称")}」，与所选商品不一致" });
            if (!布尔(批, "是否有效"))
                return BadRequest(new { message = $"批次 {字符串(批, "批次号")} 已停用，不能再调拨" });

            var 键 = $"{x.商品}/{x.批次}/{x.状态}/{x.权属}";
            需求[键] = 需求.GetValueOrDefault(键) + x.数量;
        }
        foreach (var (键, 数量) in 需求)
        {
            var s = 键.Split('/');
            var 行 = 现量.FirstOrDefault(r => 整数(r, "商品编号") == int.Parse(s[0])
                                          && 整数(r, "批次编号") == int.Parse(s[1])
                                          && 字符串(r, "状态") == s[2] && 字符串(r, "权属") == s[3]);
            var 可用 = 行 is null ? 0 : 整数(行, "可用数量");
            if (可用 >= 数量) continue;
            var 批次号 = 字符串(行 ?? 现量.FirstOrDefault(r => 整数(r, "批次编号") == int.Parse(s[1])), "批次号");
            return Conflict(new { message = $"批次 {批次号} 在「{字符串(调出, "仓库名称")}」的 {s[2]}/{s[3]} 可用 {可用}，本次要调 {数量}" });
        }

        // 成本单价：以**调出仓该五维的结存成本**为准（移动加权），前端传了也只是覆盖值
        var 行sql = new List<string>();
        var 写参 = new List<(string, object?)>
        {
            ("@out", body.调出仓库编号), ("@in", body.调入仓库编号), ("@d", 日期),
            ("@type", 跨网点 ? "跨网点" : "仓库间"), ("@cross", 跨网点), ("@through", 经过在途),
            ("@veh", body.关联车辆编号), ("@emp", _me.员工编号), ("@note", body.备注),
            ("@post", body.过账)
        };
        for (var i = 0; i < 行键.Count; i++)
        {
            var (商品, 批次, 状态, 权属, 数量, 成本, 备注) = 行键[i];
            var 行 = 现量.FirstOrDefault(r => 整数(r, "商品编号") == 商品 && 整数(r, "批次编号") == 批次
                                          && 字符串(r, "状态") == 状态 && 字符串(r, "权属") == 权属);
            var 单价 = 成本 is > 0 ? 成本.Value : (行 is null ? 0m : 十进制(行, "成本单价"));
            写参.Add(($"@p{i}", 商品));
            写参.Add(($"@b{i}", 批次));
            写参.Add(($"@s{i}", 状态));
            写参.Add(($"@w{i}", 权属));
            写参.Add(($"@q{i}", 数量));
            写参.Add(($"@c{i}", 单价));
            写参.Add(($"@n{i}", 备注));
            行sql.Add($"(@id, {i + 1}, @p{i}, @b{i}, @s{i}, @w{i}, @q{i}, @c{i}, @n{i})");
        }

        try
        {
            var r = new List<Dictionary<string, object?>>();
            await using (var tx = await _db.Database.BeginTransactionAsync(ct))
            {
                var noRow = await _data.查询SqlAsync("CALL public.\"usp_取单据编号\"('transfer',@no,@d)", ct, ("@no", null), ("@d", 日期));
                var _nr = noRow.FirstOrDefault();
                var no = _nr is not null && _nr.TryGetValue("p_单据编号", out var _nv) ? Convert.ToString(_nv) ?? "" : "";
                var hdr = await _data.查询SqlAsync("""
                    INSERT INTO "库存调拨单" ("单据号码","调出仓库编号","调入仓库编号","调拨日期","调拨类型","是否跨网点","是否经过在途","关联车辆编号","单据状态","经办人编号","备注","创建人")
                    VALUES (@no,@out,@in,@d,@type,@cross,@through,@veh,0,@emp,@note,@emp) RETURNING "调拨单编号";
                    """, ct,
                    ("@no", no), ("@out", body.调出仓库编号), ("@in", body.调入仓库编号), ("@d", 日期),
                    ("@type", 跨网点 ? "跨网点" : "仓库间"), ("@cross", 跨网点), ("@through", 经过在途),
                    ("@veh", body.关联车辆编号), ("@emp", _me.员工编号), ("@note", body.备注));
                var id = hdr.FirstOrDefault() is { } _hr && _hr.TryGetValue("调拨单编号", out var _hv) && _hv is not null ? Convert.ToInt32(_hv) : 0;
                for (var i = 0; i < 行键.Count; i++)
                {
                    var (商品, 批次, 状态, 权属, 数量, 成本, 备注) = 行键[i];
                    var 行 = 现量.FirstOrDefault(r0 => 整数(r0, "商品编号") == 商品 && 整数(r0, "批次编号") == 批次 && 字符串(r0, "状态") == 状态 && 字符串(r0, "权属") == 权属);
                    var 单价 = 成本 is > 0 ? 成本.Value : (行 is null ? 0m : 十进制(行, "成本单价"));
                    await _data.查询SqlAsync("""
                        INSERT INTO "库存调拨单明细" ("调拨单编号","行号","商品编号","批次编号","库存状态代码","权属代码","调拨数量","成本单价","备注")
                        VALUES (@id,@ln,@p,@b,@s,@w,@q,@c,@n);
                        """, ct,
                        ("@id", id), ("@ln", i + 1), ("@p", 商品), ("@b", 批次), ("@s", 状态), ("@w", 权属),
                        ("@q", 数量), ("@c", 单价), ("@n", 备注));
                }
                if (body.过账)
                {
                    await _data.查询SqlAsync("CALL public.\"usp_库存_调拨\"(@id,true)", ct, ("@id", id));
                    var ex = await _data.标量Async<int>("SELECT COUNT(1) FROM \"出入库单据池\" WHERE \"单据类型代码\"='TRANSFER' AND \"单据编号\"=@id", ct, ("@id", id));
                    if (ex == 0)
                        await _data.查询SqlAsync("""
                            INSERT INTO "出入库单据池" ("单据类型代码","单据编号","仓库编号","业务日期","方向","总数量","单据状态")
                            SELECT 'TRANSFER', @id, @out, @d, -1, COALESCE(SUM("调拨数量"),0), 3
                            FROM "库存调拨单明细" WHERE "调拨单编号"=@id;
                            """, ct, ("@id", id), ("@out", body.调出仓库编号), ("@d", 日期));
                }
                await tx.CommitAsync(ct);
                r = await _data.查询SqlAsync("""
                    SELECT @id AS "调拨单编号", @no AS "单据号码", t."单据状态", t."在途状态", t."调拨类型",
                           CAST(t."是否经过在途" AS int) AS "是否经过在途", t."调拨日期", w1."仓库名称" AS "调出仓库", w2."仓库名称" AS "调入仓库",
                           (SELECT COALESCE(SUM("调拨数量"),0) FROM "库存调拨单明细" WHERE "调拨单编号"=@id) AS "调拨总量"
                    FROM "库存调拨单" t
                    JOIN "仓库" w1 ON w1."仓库编号"=t."调出仓库编号"
                    JOIN "仓库" w2 ON w2."仓库编号"=t."调入仓库编号"
                    WHERE t."调拨单编号"=@id;
                    """, ct, ("@id", id), ("@no", no));
            }
            return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>过账一张已建的调拨单（草稿/已审核都可以）：调出仓扣减、调入仓增加，走在途时调入仓先进 TRANSIT。</summary>
    [HttpPost("transfers/{id:int}/post")]
    [需要权限("POST")]
    public async Task<IActionResult> 过账调拨(int id, CancellationToken ct)
    {
        try
        {
            var hdr = (await _data.查询SqlAsync("""
                SELECT "调拨日期", "调出仓库编号" FROM "库存调拨单" WHERE "调拨单编号" = @id;
                """, ct, ("@id", id))).FirstOrDefault();
            if (hdr is null) return Conflict(new { message = "调拨单不存在。" });
            var d0 = hdr.TryGetValue("调拨日期", out var _d) ? _d : null;
            var outWh = hdr.TryGetValue("调出仓库编号", out var _o) ? _o : null;
            await _data.查询SqlAsync("CALL public.\"usp_库存_调拨\"(@id,true)", ct, ("@id", id));
            var ex = await _data.标量Async<int>("SELECT COUNT(1) FROM \"出入库单据池\" WHERE \"单据类型代码\"='TRANSFER' AND \"单据编号\"=@id", ct, ("@id", id));
            if (ex == 0)
                await _data.查询SqlAsync("""
                    INSERT INTO "出入库单据池" ("单据类型代码","单据编号","仓库编号","业务日期","方向","总数量","单据状态")
                    SELECT 'TRANSFER', @id, @out, @d, -1, COALESCE(SUM("调拨数量"), 0), 3
                    FROM "库存调拨单明细" WHERE "调拨单编号" = @id;
                    """, ct, ("@id", id), ("@out", outWh), ("@d", d0));
            var r = await _data.查询SqlAsync("""
                SELECT "调拨单编号", "单据号码", "单据状态", "在途状态", "发出时间"
                FROM "库存调拨单" WHERE "调拨单编号" = @id;
                """, ct, ("@id", id));
            return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 在途到达：把调入仓的 TRANSIT 数量转成 AVAIL（usp_库存_到达在途，一在途行两条流水）。
    /// 差异不由这里改：过程参数 @实到数量 在实现里未被使用，实到差异请用调整单。
    /// </summary>
    [HttpPost("transfers/{id:int}/arrive")]
    [需要权限("EDIT")]
    public async Task<IActionResult> 到达调拨(int id, CancellationToken ct)
    {
        try
        {
            await _data.查询SqlAsync("CALL public.\"usp_库存_到达在途\"(@id,NULL,NULL)", ct, ("@id", id));
            var r = await _data.查询SqlAsync("""
                SELECT "调拨单编号", "单据号码", "在途状态", "实际到达时间"
                FROM "库存调拨单" WHERE "调拨单编号" = @id;
                """, ct, ("@id", id));
            return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // ================================================================ 调整

    public sealed class 调整明细行
    {
        public int 商品编号 { get; set; }
        public int 批次编号 { get; set; }
        /// <summary>正数 = 盘盈/增加，负数 = 盘亏/减少（CK_调整明细_数量 要求非 0）。</summary>
        public int 调整数量 { get; set; }
        public string? 库存状态代码 { get; set; }
        public string? 权属代码 { get; set; }
        public decimal? 成本单价 { get; set; }
        public string? 备注 { get; set; }
    }

    public sealed class 调整入参
    {
        public int 仓库编号 { get; set; }
        public DateTime? 调整日期 { get; set; }
        public string? 调整类型 { get; set; }
        /// <summary>必填：调整单是"没有单据也能改账"的唯一入口，没原因就等于没有追责依据。</summary>
        public string? 调整原因 { get; set; }
        public int? 来源盘点单编号 { get; set; }
        public string? 备注 { get; set; }
        public bool 过账 { get; set; }
        public List<调整明细行>? 明细 { get; set; }
    }

    /// <summary>调整单列表（含明细行数与净调整量）。</summary>
    [HttpGet("adjusts")]
    [资源("Inv.Adjust")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 调整列表(
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "size")] int size = 20,
        [FromQuery(Name = "keyword")] string? keyword = null,
        [FromQuery(Name = "单据状态")] int? 单据状态 = null,
        [FromQuery(Name = "调整类型")] string? 调整类型 = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);

        var 条件 = new List<string>();
        var ps = new List<(string 名, object? 值)>();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            条件.Add("(h.单据号码 LIKE @kw OR w.仓库名称 LIKE @kw OR ISNULL(h.调整原因, '') LIKE @kw)");
            ps.Add(("@kw", $"%{keyword.Trim()}%"));
        }
        if (单据状态 is not null)
        {
            条件.Add("h.单据状态 = @st");
            ps.Add(("@st", 单据状态.Value));
        }
        if (!string.IsNullOrWhiteSpace(调整类型))
        {
            条件.Add("h.调整类型 = @t");
            ps.Add(("@t", 调整类型.Trim()));
        }
        var where = 条件.Count == 0 ? "" : "WHERE " + string.Join(" AND ", 条件);

        // where 片段用的是 h./w. 别名，计数与取数共用同一套 JOIN，避免"总数与页面对不上"
        var 基 = """
            FROM dbo.[库存调整单] h
            JOIN dbo.[仓库] w ON w.[仓库编号] = h.[仓库编号]
            """;
        var total = await _data.标量Async<long>($"SELECT COUNT(1) {基} {where}", ct, ps.ToArray());

        var items = await _data.查询SqlAsync($"""
            SELECT h.[调整单编号], h.[单据号码], h.[调整日期], h.[调整类型], h.[调整原因],
                   h.[单据状态], h.[来源盘点单编号], h.[备注], h.[仓库编号], w.[仓库名称],
                   ISNULL(e.[姓名], '') AS [经办人],
                   CASE h.[单据状态] WHEN 0 THEN N'草稿' WHEN 1 THEN N'已提交' WHEN 2 THEN N'已审核'
                        WHEN 3 THEN N'已过账' WHEN 9 THEN N'已作废' ELSE N'其他' END AS [状态名称],
                   ISNULL((SELECT COUNT(1) FROM dbo.[库存调整单明细] d
                            WHERE d.[调整单编号] = h.[调整单编号]), 0) AS [行数],
                   ISNULL((SELECT SUM(d.[调整数量]) FROM dbo.[库存调整单明细] d
                            WHERE d.[调整单编号] = h.[调整单编号]), 0) AS [净调整],
                   ISNULL((SELECT dbo.fn_round(SUM(CAST(d.[行金额] AS decimal(38, 6))), 2)
                             FROM dbo.[库存调整单明细] d
                            WHERE d.[调整单编号] = h.[调整单编号]), 0) AS [金额],
                   ISNULL((SELECT COUNT(1) FROM dbo.[凭证] v
                            WHERE v.[来源单据类型] = 'ADJUST' AND v.[来源单据编号] = h.[调整单编号]), 0) AS [凭证数]
            {基}
            LEFT JOIN dbo.[员工] e ON e.[员工编号] = h.[经办人编号]
            {where}
            ORDER BY h.[调整单编号] DESC
            OFFSET {(page - 1) * size} ROWS FETCH NEXT {size} ROWS ONLY;
            """, ct, ps.ToArray());
        return Ok(new { total, items });
    }

    /// <summary>调整单详情（主表 + 明细）。</summary>
    [HttpGet("adjusts/{id:int}")]
    [资源("Inv.Adjust")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 调整详情(int id, CancellationToken ct)
    {
        var 主表 = (await _data.查询SqlAsync("""
            SELECT h.*, w.仓库名称, ISNULL(e.姓名, '') AS 经办人,
                   CASE h.单据状态 WHEN 0 THEN N'草稿' WHEN 1 THEN N'已提交' WHEN 2 THEN N'已审核'
                        WHEN 3 THEN N'已过账' WHEN 9 THEN N'已作废' ELSE N'其他' END AS 状态名称
              FROM dbo.库存调整单 h JOIN dbo.仓库 w ON w.仓库编号 = h.仓库编号
              LEFT JOIN dbo.员工 e ON e.员工编号 = h.经办人编号
             WHERE h.调整单编号 = @id;
            """, ct, ("@id", id))).FirstOrDefault();
        if (主表 is null) return NotFound(new { message = "调整单不存在" });

        var 明细 = await _data.查询SqlAsync("""
            SELECT d.明细编号, d.行号, d.商品编号, p.商品名称, d.批次编号, b.批次号,
                   d.库存状态代码, z.状态名称, d.权属代码, q.权属名称,
                   d.调整数量, d.成本单价, d.行金额, d.备注
              FROM dbo.库存调整单明细 d
              JOIN dbo.商品 p ON p.商品编号 = d.商品编号
              JOIN dbo.库存批次 b ON b.批次编号 = d.批次编号
              JOIN dbo.库存状态 z ON z.状态代码 = d.库存状态代码
              JOIN dbo.库存权属 q ON q.权属代码 = d.权属代码
             WHERE d.调整单编号 = @id ORDER BY d.行号;
            """, ct, ("@id", id));
        var 凭证 = await _data.查询SqlAsync("""
            SELECT 凭证编号, 凭证字号, 凭证日期, 摘要, 单据状态 FROM dbo.凭证
             WHERE 来源单据类型 = 'ADJUST' AND 来源单据编号 = @id ORDER BY 凭证编号;
            """, ct, ("@id", id));
        return Ok(new { 主表, 明细, 凭证 });
    }

    /// <summary>建调整单（原因必填，可选同批过账）。过账 = usp_库存_调整：正数走 ADJ_GAIN 入库、负数走 ADJ_LOSS 出库，并生成盘盈亏凭证。</summary>
    [HttpPost("adjust")]
    [资源("Inv.Adjust")]
    [需要权限("ADD")]
    public async Task<IActionResult> 建调整单([FromBody] 调整入参 body, CancellationToken ct)
    {
        var 明细 = (body.明细 ?? new List<调整明细行>()).Where(x => x.调整数量 != 0).ToList();
        if (明细.Count == 0) return BadRequest(new { message = "请至少填写一行调整明细（调整数量不能为 0）" });
        if (明细.Count > 200) return BadRequest(new { message = "单张调整单最多 200 行，请拆单" });
        if (body.仓库编号 <= 0) return BadRequest(new { message = "请选择调整仓库" });
        if (string.IsNullOrWhiteSpace(body.调整原因))
            return BadRequest(new { message = "必须填写调整原因：调整单是唯一「无业务单据也能改账」的入口，原因就是审计线索" });
        var 类型 = body.调整类型?.Trim() ?? "";
        if (!调整类型集合.Contains(类型))
            return BadRequest(new { message = $"调整类型只能选 {string.Join(" / ", 调整类型集合)}（CK_库存调整单_类型）" });

        var 日期 = body.调整日期?.Date ?? DateTime.Today;
        var 仓 = (await _data.查询SqlAsync(
            "SELECT 仓库编号, 仓库名称, 是否有效 FROM dbo.仓库 WHERE 仓库编号 = @wh", ct,
            ("@wh", body.仓库编号))).FirstOrDefault();
        if (仓 is null) return BadRequest(new { message = "仓库不存在" });
        if (!布尔(仓, "是否有效")) return BadRequest(new { message = "仓库已停用" });

        var 行键 = new List<(int 商品, int 批次, string 状态, string 权属, int 数量, decimal? 成本, string? 备注)>();
        foreach (var x in 明细)
        {
            var 状态 = (x.库存状态代码 ?? "AVAIL").Trim().ToUpperInvariant();
            var 权属 = (x.权属代码 ?? "OWN").Trim().ToUpperInvariant();
            if (x.商品编号 <= 0 || x.批次编号 <= 0) return BadRequest(new { message = "每行都必须选商品与批次" });
            if (!可作业状态.Contains(状态)) return BadRequest(new { message = $"库存状态 {状态} 不能调整" });
            if (!权属集合.Contains(权属)) return BadRequest(new { message = $"权属 {权属} 不存在" });
            行键.Add((x.商品编号, x.批次编号, 状态, 权属, x.调整数量, x.成本单价, x.备注));
        }

        var ps = new List<(string, object?)> { ("@wh", body.仓库编号) };
        var vals = new List<string>();
        for (var i = 0; i < 行键.Count; i++)
        {
            ps.Add(($"@p{i}", 行键[i].商品));
            ps.Add(($"@b{i}", 行键[i].批次));
            vals.Add($"(@p{i}, @b{i})");
        }
        var 现量 = await _data.查询SqlAsync($"""
            SELECT b.[批次编号], b.[商品编号], b.[批次号], b.[是否有效], b.[成本单价] AS [批次成本],
                   ISNULL(s.[库存状态代码], '') AS [状态], ISNULL(s.[权属代码], '') AS [权属],
                   ISNULL(s.[数量], 0) AS [数量], ISNULL(s.[成本单价], 0) AS [结存成本]
            FROM (VALUES {string.Join(",", vals)}) v([商品编号], [批次编号])
            JOIN dbo.[库存批次] b ON b.[批次编号] = v.[批次编号]
            LEFT JOIN dbo.[库存结存] s ON s.[批次编号] = b.[批次编号]
                                      AND s.[商品编号] = b.[商品编号] AND s.[仓库编号] = @wh
            """, ct, ps.ToArray());

        var 需求 = new Dictionary<string, int>();
        foreach (var x in 行键)
        {
            var 批 = 现量.FirstOrDefault(r => 整数(r, "批次编号") == x.批次);
            if (批 is null) return BadRequest(new { message = $"批次 {x.批次} 不存在" });
            if (整数(批, "商品编号") != x.商品) return BadRequest(new { message = $"批次 {字符串(批, "批次号")} 与所选商品不一致" });
            if (!布尔(批, "是否有效")) return BadRequest(new { message = $"批次 {字符串(批, "批次号")} 已停用" });
            if (x.数量 < 0)
            {
                var 键 = $"{x.商品}/{x.批次}/{x.状态}/{x.权属}";
                需求[键] = 需求.GetValueOrDefault(键) + -x.数量;
            }
        }
        foreach (var (键, 数量) in 需求)
        {
            var s = 键.Split('/');
            var 行 = 现量.FirstOrDefault(r => 整数(r, "商品编号") == int.Parse(s[0])
                                          && 整数(r, "批次编号") == int.Parse(s[1])
                                          && 字符串(r, "状态") == s[2] && 字符串(r, "权属") == s[3]);
            var 在账面 = 行 is null ? 0 : 整数(行, "数量");
            if (在账面 < 数量)
                return Conflict(new { message = $"调减 {数量} 超过「{字符串(仓, "仓库名称")}」该五维账面 {在账面}：盘亏只能把账面调到 0 以上，多减请先盘点确认" });
        }

        var 写参 = new List<(string, object?)>
        {
            ("@wh", body.仓库编号), ("@d", 日期), ("@type", 类型),
            ("@reason", body.调整原因.Trim()), ("@src", body.来源盘点单编号),
            ("@emp", _me.员工编号), ("@note", body.备注), ("@post", body.过账)
        };
        var 行sql = new List<string>();
        for (var i = 0; i < 行键.Count; i++)
        {
            var (商品, 批次, 状态, 权属, 数量, 成本, 备注) = 行键[i];
            var 行 = 现量.FirstOrDefault(r => 整数(r, "商品编号") == 商品 && 整数(r, "批次编号") == 批次
                                          && 字符串(r, "状态") == 状态 && 字符串(r, "权属") == 权属);
            decimal 单价;
            if (成本 is > 0) 单价 = 成本.Value;
            else if (数量 < 0) 单价 = 行 is null ? 0m : 十进制(行, "结存成本");   // 调减必须按账面成本出，否则流水金额与结存对不上
            else if (行 is not null) 单价 = 十进制(行, "结存成本");
            else 单价 = 十进制(现量.First(r => 整数(r, "批次编号") == 批次), "批次成本");
            写参.Add(($"@p{i}", 商品));
            写参.Add(($"@b{i}", 批次));
            写参.Add(($"@s{i}", 状态));
            写参.Add(($"@w{i}", 权属));
            写参.Add(($"@q{i}", 数量));
            写参.Add(($"@c{i}", 单价));
            写参.Add(($"@n{i}", 备注));
            行sql.Add($"(@id, {i + 1}, @p{i}, @b{i}, @s{i}, @w{i}, @q{i}, @c{i}, @n{i})");
        }

        try
        {
            var r = new List<Dictionary<string, object?>>();
            await using (var tx = await _db.Database.BeginTransactionAsync(ct))
            {
                var noRow = await _data.查询SqlAsync("CALL public.\"usp_取单据编号\"('adjust',@no,@d)", ct, ("@no", null), ("@d", 日期));
                var _nr = noRow.FirstOrDefault();
                var no = _nr is not null && _nr.TryGetValue("p_单据编号", out var _nv) ? Convert.ToString(_nv) ?? "" : "";
                var hdr = await _data.查询SqlAsync("""
                    INSERT INTO "库存调整单" ("单据号码","仓库编号","调整日期","调整类型","来源盘点单编号","调整原因","单据状态","经办人编号","备注","创建人")
                    VALUES (@no,@wh,@d,@type,@src,@reason,0,@emp,@note,@emp) RETURNING "调整单编号";
                    """, ct,
                    ("@no", no), ("@wh", body.仓库编号), ("@d", 日期), ("@type", 类型), ("@src", body.来源盘点单编号),
                    ("@reason", body.调整原因.Trim()), ("@emp", _me.员工编号), ("@note", body.备注));
                var id = hdr.FirstOrDefault() is { } _hr && _hr.TryGetValue("调整单编号", out var _hv) && _hv is not null ? Convert.ToInt32(_hv) : 0;
                for (var i = 0; i < 行键.Count; i++)
                {
                    var (商品, 批次, 状态, 权属, 数量, 成本, 备注) = 行键[i];
                    var 行 = 现量.FirstOrDefault(r0 => 整数(r0, "商品编号") == 商品 && 整数(r0, "批次编号") == 批次 && 字符串(r0, "状态") == 状态 && 字符串(r0, "权属") == 权属);
                    decimal 单价;
                    if (成本 is > 0) 单价 = 成本.Value;
                    else if (数量 < 0) 单价 = 行 is null ? 0m : 十进制(行, "结存成本");
                    else if (行 is not null) 单价 = 十进制(行, "结存成本");
                    else 单价 = 十进制(现量.First(r0 => 整数(r0, "批次编号") == 批次), "批次成本");
                    await _data.查询SqlAsync("""
                        INSERT INTO "库存调整单明细" ("调整单编号","行号","商品编号","批次编号","库存状态代码","权属代码","调整数量","成本单价","备注")
                        VALUES (@id,@ln,@p,@b,@s,@w,@q,@c,@n);
                        """, ct,
                        ("@id", id), ("@ln", i + 1), ("@p", 商品), ("@b", 批次), ("@s", 状态), ("@w", 权属),
                        ("@q", 数量), ("@c", 单价), ("@n", 备注));
                }
                if (body.过账)
                {
                    await _data.查询SqlAsync("CALL public.\"usp_库存_调整\"(@id)", ct, ("@id", id));
                    var ex = await _data.标量Async<int>("SELECT COUNT(1) FROM \"出入库单据池\" WHERE \"单据类型代码\"='ADJUST' AND \"单据编号\"=@id", ct, ("@id", id));
                    if (ex == 0)
                        await _data.查询SqlAsync("""
                            INSERT INTO "出入库单据池" ("单据类型代码","单据编号","仓库编号","业务日期","方向","总数量","单据状态")
                            SELECT 'ADJUST', @id, @wh, @d,
                                   CASE WHEN SIGN(COALESCE(SUM("调整数量"), 1)) = 0 THEN 1 ELSE SIGN(COALESCE(SUM("调整数量"), 1)) END,
                                   ABS(COALESCE(SUM("调整数量"), 0)), 3
                            FROM "库存调整单明细" WHERE "调整单编号"=@id;
                            """, ct, ("@id", id), ("@wh", body.仓库编号), ("@d", 日期));
                }
                await tx.CommitAsync(ct);
                r = await _data.查询SqlAsync("""
                    SELECT @id AS "调整单编号", @no AS "单据号码", "调整日期", "调整类型", "调整原因", "单据状态", "仓库编号",
                           (SELECT COALESCE(SUM("调整数量"),0) FROM "库存调整单明细" WHERE "调整单编号"=@id) AS "净调整"
                    FROM "库存调整单" WHERE "调整单编号"=@id;
                    """, ct, ("@id", id), ("@no", no));
            }
            return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>过账一张已建的调整单（正数入库、负数出库，并生成盘盈/盘亏凭证）。</summary>
    [HttpPost("adjusts/{id:int}/post")]
    [资源("Inv.Adjust")]
    [需要权限("POST")]
    public async Task<IActionResult> 过账调整(int id, CancellationToken ct)
    {
        try
        {
            var hdr = (await _data.查询SqlAsync("""
                SELECT "仓库编号", "调整日期" FROM "库存调整单" WHERE "调整单编号" = @id;
                """, ct, ("@id", id))).FirstOrDefault();
            if (hdr is null) return Conflict(new { message = "调整单不存在。" });
            var wh0 = hdr.TryGetValue("仓库编号", out var _w) ? _w : null;
            var d0 = hdr.TryGetValue("调整日期", out var _dd) ? _dd : null;
            await _data.查询SqlAsync("CALL public.\"usp_库存_调整\"(@id)", ct, ("@id", id));
            var ex = await _data.标量Async<int>("SELECT COUNT(1) FROM \"出入库单据池\" WHERE \"单据类型代码\"='ADJUST' AND \"单据编号\"=@id", ct, ("@id", id));
            if (ex == 0)
                await _data.查询SqlAsync("""
                    INSERT INTO "出入库单据池" ("单据类型代码","单据编号","仓库编号","业务日期","方向","总数量","单据状态")
                    SELECT 'ADJUST', @id, @wh, @d,
                           CASE WHEN SIGN(COALESCE(SUM("调整数量"), 1)) = 0 THEN 1 ELSE SIGN(COALESCE(SUM("调整数量"), 1)) END,
                           ABS(COALESCE(SUM("调整数量"), 0)), 3
                    FROM "库存调整单明细" WHERE "调整单编号" = @id;
                    """, ct, ("@id", id), ("@wh", wh0), ("@d", d0));
            var r = await _data.查询SqlAsync("""
                SELECT "调整单编号", "单据号码", "单据状态" FROM "库存调整单" WHERE "调整单编号" = @id;
                """, ct, ("@id", id));
            return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // ================================================================ 冻结

    public sealed class 冻结入参
    {
        public int 仓库编号 { get; set; }
        public int 商品编号 { get; set; }
        /// <summary>必填：五维冻结要给到批次，留空时库里过程会退化成"该商品最小批次"，那是错的维度。</summary>
        public int? 批次编号 { get; set; }
        public string? 库存状态代码 { get; set; }
        public string? 权属代码 { get; set; }
        public int 冻结数量 { get; set; }
        public string? 冻结原因 { get; set; }
        public string? 冻结来源 { get; set; }
        public string? 备注 { get; set; }
    }

    /// <summary>冻结单列表（含冻结中数量、是否已解冻）。</summary>
    [HttpGet("freezes")]
    [资源("Inv.Adjust")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 冻结列表(
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "size")] int size = 20,
        [FromQuery(Name = "keyword")] string? keyword = null,
        [FromQuery(Name = "单据状态")] int? 单据状态 = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);

        var 条件 = new List<string>();
        var ps = new List<(string 名, object? 值)>();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            条件.Add("(f.单据号码 LIKE @kw OR f.冻结原因 LIKE @kw OR w.仓库名称 LIKE @kw OR p.商品名称 LIKE @kw)");
            ps.Add(("@kw", $"%{keyword.Trim()}%"));
        }
        if (单据状态 is not null)
        {
            条件.Add("f.单据状态 = @st");
            ps.Add(("@st", 单据状态.Value));
        }
        var where = 条件.Count == 0 ? "" : "WHERE " + string.Join(" AND ", 条件);

        var 基 = """
            FROM dbo.[库存冻结单] f
            JOIN dbo.[仓库] w ON w.[仓库编号] = f.[仓库编号]
            JOIN dbo.[商品] p ON p.[商品编号] = f.[商品编号]
            LEFT JOIN dbo.[库存批次] b ON b.[批次编号] = f.[批次编号]
            LEFT JOIN dbo.[员工] e ON e.[员工编号] = f.[经办人编号]
            """;
        var total = await _data.标量Async<long>($"SELECT COUNT(1) {基} {where}", ct, ps.ToArray());
        var items = await _data.查询SqlAsync($"""
            SELECT f.[冻结单编号], f.[单据号码], f.[仓库编号], w.[仓库名称], f.[商品编号], p.[商品名称],
                   f.[批次编号], ISNULL(b.[批次号], '') AS [批次号], f.[冻结数量], f.[已解冻数量], f.[冻结中数量],
                   f.[冻结原因], f.[冻结来源], f.[冻结时间], f.[解冻时间], f.[单据状态], f.[备注],
                   ISNULL(e.[姓名], '') AS [经办人],
                   CASE f.[单据状态] WHEN 1 THEN N'冻结中' WHEN 2 THEN N'已审核' WHEN 9 THEN N'已解冻'
                        ELSE N'其他' END AS [状态名称]
            {基} {where}
            ORDER BY f.[冻结单编号] DESC
            OFFSET {(page - 1) * size} ROWS FETCH NEXT {size} ROWS ONLY;
            """, ct, ps.ToArray());
        return Ok(new { total, items });
    }

    /// <summary>
    /// 冻结：可用 → 冻结（同仓同商品同批次同权属，两条流水，合计数量不变）。
    /// 批次必填（过程留空会退化到该商品最小批次，那不是用户点的五维）；
    /// 超可用由 usp_库存_出 在 UPDLOCK 下拦（53133）。
    /// </summary>
    [HttpPost("freeze")]
    [资源("Inv.Adjust")]
    [需要权限("ADD")]
    public async Task<IActionResult> 建冻结单([FromBody] 冻结入参 body, CancellationToken ct)
    {
        if (body.仓库编号 <= 0 || body.商品编号 <= 0 || body.批次编号 is not > 0)
            return BadRequest(new { message = "五维冻结要给全：仓库、商品、批次都要选" });
        if (body.冻结数量 <= 0) return BadRequest(new { message = "冻结数量必须大于 0" });
        if (string.IsNullOrWhiteSpace(body.冻结原因))
            return BadRequest(new { message = "必须填写冻结原因（质检不合格 / 纠纷 / 盘点 / 其他）" });
        var 来源 = string.IsNullOrWhiteSpace(body.冻结来源) ? "质检" : body.冻结来源.Trim();
        if (!冻结来源集合.Contains(来源))
            return BadRequest(new { message = $"冻结来源只能选 {string.Join(" / ", 冻结来源集合)}" });
        var 状态 = (body.库存状态代码 ?? "AVAIL").Trim().ToUpperInvariant();
        var 权属 = (body.权属代码 ?? "OWN").Trim().ToUpperInvariant();
        if (!可作业状态.Contains(状态)) return BadRequest(new { message = $"库存状态 {状态} 不能作为冻结来源" });
        if (!权属集合.Contains(权属)) return BadRequest(new { message = $"权属 {权属} 不存在" });

        var 行 = (await _data.查询SqlAsync("""
            SELECT s.数量, s.可用数量, s.成本单价, b.批次号, w.仓库名称, w.是否有效
              FROM dbo.库存结存 s
              JOIN dbo.库存批次 b ON b.批次编号 = s.批次编号 AND b.商品编号 = s.商品编号
              JOIN dbo.仓库 w ON w.仓库编号 = s.仓库编号
             WHERE s.仓库编号 = @wh AND s.商品编号 = @p AND s.批次编号 = @b
               AND s.库存状态代码 = @st AND s.权属代码 = @own;
            """, ct, ("@wh", body.仓库编号), ("@p", body.商品编号), ("@b", body.批次编号!.Value),
               ("@st", 状态), ("@own", 权属))).FirstOrDefault();
        if (行 is null)
            return Conflict(new { message = "该五维（仓库 × 商品 × 批次 × 状态 × 权属）在账上没有结存，无法冻结" });
        if (!布尔(行, "是否有效")) return BadRequest(new { message = "仓库已停用" });
        if (十进制(行, "可用数量") < body.冻结数量)
            return Conflict(new { message = $"可用 {整数(行, "可用数量")}，不能冻结 {body.冻结数量}" });

        try
        {
            var noRow = await _data.查询SqlAsync("CALL public.\"usp_取单据编号\"('freeze',@no,@d)", ct, ("@no", null), ("@d", DateTime.Today));
            var _nr = noRow.FirstOrDefault();
            var no = _nr is not null && _nr.TryGetValue("p_单据编号", out var _nv) ? Convert.ToString(_nv) ?? "" : "";
            var docRow = await _data.查询SqlAsync("""
                INSERT INTO "库存冻结单" ("单据号码","仓库编号","商品编号","批次编号","冻结数量","冻结原因","冻结来源","单据状态","经办人编号","备注","创建人")
                VALUES (@no,@wh,@p,@b,@qty,@reason,@src,1,@emp,@note,@emp) RETURNING "冻结单编号";
                """, ct,
                ("@no", no), ("@wh", body.仓库编号), ("@p", body.商品编号), ("@b", body.批次编号),
                ("@qty", body.冻结数量), ("@reason", body.冻结原因.Trim()), ("@src", 来源),
                ("@emp", _me.员工编号), ("@note", body.备注));
            var doc = docRow.FirstOrDefault() is { } _dr && _dr.TryGetValue("冻结单编号", out var _dv) && _dv is not null ? Convert.ToInt32(_dv) : 0;
            await _data.查询SqlAsync("CALL public.\"usp_库存_冻结\"(@id,false)", ct, ("@id", doc));
            var r = await _data.查询SqlAsync("""
                SELECT @id AS "冻结单编号", @no AS "单据号码", "冻结数量", "冻结中数量", "单据状态", "冻结时间"
                FROM "库存冻结单" WHERE "冻结单编号" = @id;
                """, ct, ("@id", doc), ("@no", no));
            return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>解冻：冻结 → 可用（usp_库存_冻结 @是否解冻=1，整单剩余量一次放回，状态 9）。</summary>
    [HttpPost("freezes/{id:int}/unfreeze")]
    [资源("Inv.Adjust")]
    [需要权限("EDIT")]
    public async Task<IActionResult> 解冻(int id, CancellationToken ct)
    {
        try
        {
            await _data.查询SqlAsync("CALL public.\"usp_库存_冻结\"(@id,true)", ct, ("@id", id));
            var r = await _data.查询SqlAsync("""
                SELECT "冻结单编号", "单据号码", "冻结数量", "已解冻数量", "冻结中数量", "单据状态", "解冻时间"
                FROM "库存冻结单" WHERE "冻结单编号" = @id;
                """, ct, ("@id", id));
            return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // ================================================================ 取值小工具

    private static int 整数(IReadOnlyDictionary<string, object?>? row, string 列)
    {
        if (row is null || !row.TryGetValue(列, out var v) || v is null || v is DBNull) return 0;
        return Convert.ToInt32(v);
    }

    private static decimal 十进制(IReadOnlyDictionary<string, object?> row, string 列)
    {
        if (!row.TryGetValue(列, out var v) || v is null || v is DBNull) return 0m;
        return Convert.ToDecimal(v);
    }

    private static string 字符串(IReadOnlyDictionary<string, object?>? row, string 列)
    {
        if (row is null || !row.TryGetValue(列, out var v) || v is null || v is DBNull) return "";
        return Convert.ToString(v) ?? "";
    }

    private static bool 布尔(IReadOnlyDictionary<string, object?> row, string 列)
    {
        if (!row.TryGetValue(列, out var v) || v is null || v is DBNull) return false;
        return v is bool b ? b : Convert.ToInt64(v) != 0;
    }
}
