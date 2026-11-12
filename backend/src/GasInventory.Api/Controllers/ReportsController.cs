using GasInventory.Api.Auth;
using GasInventory.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GasInventory.Api.Controllers;

/// <summary>
/// 报表：全部用 T-SQL 重写（Access 的 Last()/Nz()/IIf() 等函数在 SQL Server 不存在，
/// 对应写法：Last -> MAX、Nz -> ISNULL、IIf -> CASE WHEN）。
///
/// 批次 20：每个接口挂资源，且**与前端菜单/路由登记同源**（见 `路由资源表`）——
/// 「页面进得来、接口 403」和「页面进不去、接口却放行」都是同一类缺陷。
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly 数据服务 _data;

    public ReportsController(数据服务 data) => _data = data;

    /// <summary>
    /// 客户对账单：期初 + 期间送气明细 + 合计（批次 7 改 v3 列）。
    ///
    /// 原实现读 送气日期 / 金额合计 / 销量合计 / 钢瓶规格 —— 全是 v2 列，接口 500，
    /// 「客户对账单」菜单点开必然报错。现在读 v3：送气单{送货日期, 销售金额, 销售数量} +
    /// 送气单明细{数量, 单价, 行金额, 回收空瓶数} + 商品{商品名称, 计量单位}。
    ///
    /// 期初/期末应收取 **vw_应收账龄.未收金额** 按单据日期归集：
    ///   期初 = 单据日期 &lt; 起 的未收合计；期末 = 单据日期 &lt;= 止 的未收合计。
    /// 注意这是"按当前收款状态回溯"的口径（已收金额是累计值，没有历史快照），
    /// 对已结清的账期是准确的；对跨期部分收款只能作参考 —— 不拿别的数字硬凑。
    /// </summary>
    [HttpGet("customer-statement")]
    [资源("Sale.Statement")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 客户对账单(int 客户编号, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var 起 = (from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var 止 = (to ?? DateTime.Today).Date;

        var 客户 = await _data.查询SqlAsync(
            "SELECT 客户编号, 客户编码, 客户简称, 客户全称, 联系人, 联系电话, 客户地址 FROM dbo.客户 WHERE 客户编号 = @id",
            ct, ("@id", 客户编号));

        var 明细 = await _data.查询SqlAsync("""
            SELECT CONVERT(char(10), s.送货日期, 23) AS 日期,
                   s.单据号码,
                   ISNULL(p.商品名称, '')            AS 规格,
                   ISNULL(d.数量, 0)                 AS 数量,
                   ISNULL(p.计量单位, '')            AS 单位,
                   ISNULL(d.单价, 0)                 AS 单价,
                   ISNULL(d.行金额, 0)               AS 金额,
                   ISNULL(d.回收空瓶数, 0)           AS 空瓶数量
            FROM dbo.送气单 s
            JOIN dbo.送气单明细 d ON d.送气单编号 = s.送气单编号
            LEFT JOIN dbo.商品 p  ON p.商品编号   = d.商品编号
            WHERE s.客户编号 = @id AND s.送货日期 >= @f AND s.送货日期 < @t
            ORDER BY s.送货日期, s.送气单编号, d.行号
            """, ct, ("@id", 客户编号), ("@f", 起), ("@t", 止.AddDays(1)));

        var 合计 = await _data.查询SqlAsync("""
            SELECT COUNT(1) AS 单数, ISNULL(SUM(销售金额), 0) AS 金额, ISNULL(SUM(销售数量), 0) AS 销量
            FROM dbo.送气单
            WHERE 客户编号 = @id AND 送货日期 >= @f AND 送货日期 < @t
            """, ct, ("@id", 客户编号), ("@f", 起), ("@t", 止.AddDays(1)));

        var 应收 = await _data.查询SqlAsync("""
            SELECT ISNULL(SUM(CASE WHEN 单据日期 <  @f THEN 未收金额 ELSE 0 END), 0) AS 期初应收,
                   ISNULL(SUM(CASE WHEN 单据日期 <= @t THEN 未收金额 ELSE 0 END), 0) AS 期末应收
            FROM dbo.vw_应收账龄
            WHERE 客户编号 = @id AND 单据日期 < @t2
            """, ct, ("@id", 客户编号), ("@f", 起), ("@t", 止), ("@t2", 止.AddDays(1)));

        return Ok(new
        {
            客户 = 客户.FirstOrDefault(),
            明细,
            合计 = 合计.FirstOrDefault(),
            期初应收 = 应收.FirstOrDefault()?["期初应收"] ?? 0,
            期末应收 = 应收.FirstOrDefault()?["期末应收"] ?? 0,
            起,
            止
        });
    }

    /// <summary>
    /// 供气商对账单（批次 7 改 v3 列）。
    ///
    /// v2 的 进气单主干 / 进气单明细 / 供气商 / 打气车 / 钢瓶规格 在 v3 已换成
    /// 到货单 / 到货单明细 / 供应商 / 车辆 / 商品，原 SQL 直接 500。
    ///
    /// 字段映射（保持前端列名不变）：
    ///   实瓶数量 ← 到货单明细.实收数量
    ///   单瓶合算价格 ← 到货单明细.含税单价
    ///   金额 ← 不含税金额 + 税额（含税，与看板「进气」同一口径）
    ///   单瓶气重公斤 ← 商品气瓶属性.额定气重公斤（v3 没有到货明细级的实际气重列）
    /// </summary>
    [HttpGet("supplier-statement")]
    [资源("Pur.Order")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 供气商对账单(int 供气商编号, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var 起 = (from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var 止 = (to ?? DateTime.Today).Date;

        var 供应 = await _data.查询SqlAsync(
            "SELECT 供应商编号 AS 供气商编号, 供应商简称 AS 简称, 供应商全称, 联系电话 FROM dbo.供应商 WHERE 供应商编号 = @id",
            ct, ("@id", 供气商编号));

        var 明细 = await _data.查询SqlAsync("""
            SELECT CONVERT(char(10), h.到货日期, 23) AS 日期,
                   h.单据号码,
                   ISNULL(h.车牌, '')                       AS 车牌,
                   ISNULL(p.商品名称, '')                   AS 规格,
                   ISNULL(d.实收数量, 0)                    AS 实瓶数量,
                   ISNULL(a.额定气重公斤, 0)                AS 单瓶气重公斤,
                   ISNULL(d.含税单价, 0)                    AS 单瓶合算价格,
                   ISNULL(d.不含税金额, 0) + ISNULL(d.税额, 0) AS 金额
            FROM dbo.到货单 h
            JOIN dbo.到货单明细 d ON d.到货单编号 = h.到货单编号
            LEFT JOIN dbo.商品 p          ON p.商品编号 = d.商品编号
            LEFT JOIN dbo.商品气瓶属性 a  ON a.商品编号 = d.商品编号
            WHERE h.供应商编号 = @id AND h.到货日期 >= @f AND h.到货日期 < @t
            ORDER BY h.到货日期, h.到货单编号, d.行号
            """, ct, ("@id", 供气商编号), ("@f", 起), ("@t", 止.AddDays(1)));

        var 合计 = await _data.查询SqlAsync("""
            SELECT COUNT(DISTINCT h.到货单编号) AS 单数,
                   ISNULL(SUM(ISNULL(d.不含税金额, 0) + ISNULL(d.税额, 0)), 0) AS 金额,
                   ISNULL(SUM(ISNULL(d.实收数量, 0)), 0) AS 实瓶
            FROM dbo.到货单 h
            JOIN dbo.到货单明细 d ON d.到货单编号 = h.到货单编号
            WHERE h.供应商编号 = @id AND h.到货日期 >= @f AND h.到货日期 < @t
            """, ct, ("@id", 供气商编号), ("@f", 起), ("@t", 止.AddDays(1)));

        return Ok(new { 供气商 = 供应.FirstOrDefault(), 明细, 合计 = 合计.FirstOrDefault(), 起, 止 });
    }

    /// <summary>
    /// 送瓶员计提汇总 + 明细（批次 7 改 v3 视图）。
    ///
    /// 原实现整块是 v2 口径（送气日期 / 实瓶数量 / 送气员工编号 / 计提方案明细 / 钢瓶规格），
    /// 而 v3 把提成固化成 提成结算单 + 提成明细，口径由阶段 7 的过程写进库，接口 500。
    ///
    /// 现在读 v3 视图：**vw_提成计算过程**（11,442 行，逐行一条送气明细，带 送气单号 /
    /// 商品名称 / 数量 / 单位提成 / 提成金额 / 业务日期）。
    /// 为什么明细不用 vw_提成明细报表：它是同一批数据按「业务日期 × 员工 × 商品 × 规则」
    /// 聚合后的 2,709 行，**没有 单据号码 / 客户**，前端「单据号码」「客户」两列会整列空白；
    /// 计算过程是它的逐行底表，用同一张表出汇总与明细，两张表的口径也不会漂移。
    ///
    /// ⚠ 口径不动：提成只算已签收（阶段 7 定的），视图里已固化，这里不再加任何过滤条件。
    /// </summary>
    [HttpGet("commission")]
    [资源("Rpt.Commission")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 计提汇总(DateTime? from, DateTime? to, int? 员工编号, CancellationToken ct)
    {
        var 起 = (from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var 止 = (to ?? DateTime.Today).Date;
        var 参数 = new[] { ("@f", (object?)起), ("@t", (object?)止.AddDays(1)), ("@emp", (object?)(员工编号 ?? 0)) };

        var 汇总 = await _data.查询SqlAsync("""
            SELECT ISNULL(姓名, '(未指定)')     AS 送瓶员,
                   COUNT(DISTINCT 送气单编号)   AS 单数,
                   ISNULL(SUM(数量), 0)         AS 数量,
                   ISNULL(SUM(提成金额), 0)     AS 提成
            FROM dbo.vw_提成计算过程
            WHERE 业务日期 >= @f AND 业务日期 < @t
              AND (@emp = 0 OR 员工编号 = @emp)
            GROUP BY 姓名
            ORDER BY 提成 DESC
            """, ct, 参数);

        var 明细 = await _data.查询SqlAsync("""
            SELECT CONVERT(char(10), p.业务日期, 23) AS 日期,
                   ISNULL(p.送气单号, '')            AS 单据号码,
                   ISNULL(c.客户简称, '')            AS 客户,
                   ISNULL(p.姓名, '')                AS 送瓶员,
                   ISNULL(p.商品名称, '')            AS 规格,
                   ISNULL(p.数量, 0)                 AS 数量,
                   ISNULL(p.单位提成, 0)             AS 单瓶提成,
                   ISNULL(p.提成金额, 0)             AS 提成
            FROM dbo.vw_提成计算过程 p
            LEFT JOIN dbo.送气单 s ON s.送气单编号 = p.送气单编号
            LEFT JOIN dbo.客户 c   ON c.客户编号   = s.客户编号
            WHERE p.业务日期 >= @f AND p.业务日期 < @t
              AND (@emp = 0 OR p.员工编号 = @emp)
            ORDER BY p.业务日期, p.送气单编号
            """, ct, 参数);

        return Ok(new { 汇总, 明细, 起, 止 });
    }

    /// <summary>
    /// 配送清单（批次 7 改 v3 视图）。
    ///
    /// 原实现读 送气日期 / 送气员工编号 / 钢瓶规格 / 明细.金额 —— 全是 v2 列，接口 500。
    /// 现在读 **vw_销售明细**（11,523 行，一行 = 一张送气单的一个商品行，
    /// 带 送货日期 / 单据号码 / 客户 / 商品名称 / 销售数量 / 销售金额 / 签收状态 / 行号），
    /// 再补三个抬头字段：地址与电话取 客户，送瓶员取 送气单.业务员编号 → 员工.姓名。
    ///
    /// 收瓶量：v3 把空瓶回收记在 **送气单.回收空瓶数（整单级）**，视图里没有该列。
    /// 逐行照抄会把整单的空瓶数按明细行数放大，所以只在每张单的第一行给出，其余行记 0
    /// —— 前端是按行求和的，这样合计才等于真实回收数。
    /// </summary>
    [HttpGet("delivery-list")]
    [资源("Dist.Task")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 配送清单(DateTime? 日期, int? 员工编号, CancellationToken ct)
    {
        var 日 = (日期 ?? DateTime.Today).Date;
        var rows = await _data.查询SqlAsync("""
            SELECT ISNULL(e.姓名, '')        AS 送瓶员,
                   d.单据号码,
                   ISNULL(d.客户简称, '')     AS 客户,
                   ISNULL(c.客户地址, '')     AS 地址,
                   ISNULL(c.联系电话, '')     AS 电话,
                   ISNULL(d.商品名称, '')     AS 规格,
                   ISNULL(d.销售数量, 0)      AS 送气量,
                   CASE WHEN d.行号 = MIN(d.行号) OVER (PARTITION BY d.送气单编号)
                        THEN ISNULL(s.回收空瓶数, 0) ELSE 0 END AS 收瓶量,
                   ISNULL(d.销售金额, 0)      AS 金额,
                   ISNULL(d.签收状态, '')     AS 签收状态
            FROM dbo.vw_销售明细 d
            LEFT JOIN dbo.送气单 s ON s.送气单编号 = d.送气单编号
            LEFT JOIN dbo.客户 c   ON c.客户编号   = d.客户编号
            LEFT JOIN dbo.员工 e   ON e.员工编号   = s.业务员编号
            WHERE d.送货日期 >= @d AND d.送货日期 < DATEADD(day, 1, @d)
              AND (@emp = 0 OR s.业务员编号 = @emp)
            ORDER BY 送瓶员, d.单据号码, d.行号
            """, ct, ("@d", 日), ("@emp", 员工编号 ?? 0));
        return Ok(new { 日期 = 日, rows });
    }

    /// <summary>
    /// 预警：客户最后一次充气距今天数（批次 7 改 v3 视图）。
    ///
    /// 原实现读 客户.是否有效 + 客户区域（v2 表名）→ 500。v3 里客户区域仍在，
    /// 但客户与区域名称已经在 **vw_客户** 里拼好；最后充气与复购画像在 **vw_客户复购**。
    ///
    /// ⚠ 阈值语义保持不变：入参 `天数` 是「多久没交易算流失」，**不是账龄**。
    /// 分级仍在后端判（间隔天数 ≥ 天数 → 风险名单），前端只负责上色。
    /// </summary>
    [HttpGet("alerts")]
    [资源("Md.Customer")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 预警(int 天数 = 30, CancellationToken ct = default)
    {
        var rows = await _data.查询SqlAsync("""
            SELECT c.客户编号,
                   ISNULL(c.客户简称, '')  AS 客户,
                   ISNULL(c.联系电话, '')  AS 电话,
                   ISNULL(c.区域名称, '')  AS 区域,
                   r.最后送气日期          AS 最后充气,
                   DATEDIFF(day, r.最后送气日期, GETDATE()) AS 间隔天数,
                   (SELECT COUNT(1) FROM dbo.送气单 s
                     WHERE s.客户编号 = c.客户编号
                       AND s.是否已过账 = 1
                       AND s.送货日期 >= DATEADD(day, -30, GETDATE())) AS 近30天次数
            FROM dbo.vw_客户 c
            LEFT JOIN dbo.vw_客户复购 r ON r.客户编号 = c.客户编号
            WHERE c.是否有效 = 1
            ORDER BY 间隔天数 DESC
            """, ct);

        var 风险 = rows.Where(r => r["间隔天数"] is int d && d >= 天数).ToList();
        return Ok(new { 全部 = rows, 风险, 阈值 = 天数 });
    }

    /// <summary>
    /// 盘点差异：取日期区间内**已过账**盘点单的明细行（含相符行——前端「只看差异」再筛，
    /// 差异率分母也要用到账面合计）。账面/实盘/差异直接取明细，不按结存反推。
    /// </summary>
    [HttpGet("stocktake-diff")]
    [资源("Inv.Stocktake")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 盘点差异(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var 起 = (from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var 止 = (to ?? DateTime.Today).Date;

        var rows = await _data.查询SqlAsync("""
            SELECT h.盘点日期,
                   w.仓库名称       AS 盘点区域,
                   p.商品名称       AS 规格,
                   d.账面数量       AS 账面,
                   ISNULL(d.实盘数量, 0) AS 实盘,
                   ISNULL(d.差异数量, 0) AS 差异,
                   ISNULL(d.差异金额, 0) AS 差异金额,
                   ISNULL(d.差异原因, N'') AS 差异原因
            FROM dbo.库存盘点单 h
            JOIN dbo.库存盘点单明细 d ON d.盘点单编号 = h.盘点单编号
            JOIN dbo.商品 p ON p.商品编号 = d.商品编号
            JOIN dbo.仓库 w ON w.仓库编号 = h.仓库编号
            WHERE h.单据状态 = 3 AND h.盘点日期 >= @from AND h.盘点日期 <= @to
            ORDER BY h.盘点日期, h.单据号码, d.行号
            """, ct, ("@from", 起), ("@to", 止));

        return Ok(new { rows });
    }
}
