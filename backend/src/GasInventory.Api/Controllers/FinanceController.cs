using System.Text.Json;
using GasInventory.Api.Auth;
using GasInventory.Api.Services;
using GasInventory.Infrastructure.Data;
using GasInventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace GasInventory.Api.Controllers;

/// <summary>
/// 结账（批次 16）：手工凭证 / 凭证红冲 / 期间管理（月结 · 反月结 · 结转损益 · 成本结转）。
///
/// 铁律：**所有写操作都只调用库里的过程**，API 不直接 UPDATE 凭证/会计期间。
///   · 手工凭证 → usp_财务_手工凭证 + usp_财务_加分录（逐行）
///   · 过账     → usp_财务_凭证过账（借贷不平/无分录由它拦）
///   · 红冲     → usp_财务_红冲（借贷对调 + 原凭证作废）
///   · 月结     → usp_财务_月结（内部再调 成本结转 / 结转损益）
/// 为什么不让 C# 自己拼 INSERT：借贷平衡、期间锁、已记账不可改这些规则全在过程和
/// 触发器里（唯一来源）。绕过去写表，等于把三套校验抄一遍并且一定会漂。
///
/// 前置校验也不许跳：过程的 THROW(544xx) 一律原样翻成 409 + 中文原因给前端，
/// 前端只负责"把不合法的按钮藏起来"，真正的门槛永远在库里。
/// </summary>
[ApiController]
[Route("api/finance")]
[Authorize]
public class FinanceController : ControllerBase
{
    private readonly 数据服务 _data;
    private readonly 当前登录 _me;
    private readonly ILogger<FinanceController> _log;
    private readonly GasInventoryDbContext _db;

    public FinanceController(数据服务 数据, 当前登录 me, ILogger<FinanceController> log, GasInventoryDbContext db)
    {
        _data = 数据;
        _me = me;
        _log = log;
        _db = db;
    }

    private static readonly JsonSerializerOptions 分录序列化 = new() { PropertyNamingPolicy = null };

    /* ===================================================================== */
    /* 凭证台账                                                              */
    /* ===================================================================== */

    /// <summary>
    /// 凭证台账（凭证主表口径，一张凭证一行）。
    ///
    /// 「可红冲 / 可过账」由**库里的规则**算出来，不是前端猜的：
    ///   · 可过账 = 草稿(0) 且 期间未结账（已记账不可再过账，已作废不可过账）
    ///   · 可红冲 = 已记账(2) 且 未被红冲过 且 期间未结账
    ///     期间已结账不可红冲**不是我们加的限制**：usp_财务_红冲 会把原凭证置为已作废，
    ///     这一步是"改已结账期间的凭证"，被 trg_凭证_期间锁定 以 54408 拦住 ——
    ///     所以按钮只在合法时出现，不合法时给出原因（而不是让人点一次吃个 500）。
    /// </summary>
    [HttpGet("vouchers")]
    [资源("Fin.Voucher")]
    [需要权限("view")]
    public async Task<IActionResult> 凭证列表(
        CancellationToken ct,
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "size")] int size = 50,
        [FromQuery(Name = "keyword")] string? keyword = null,
        [FromQuery(Name = "from")] DateTime? from = null,
        [FromQuery(Name = "to")] DateTime? to = null,
        [FromQuery(Name = "状态")] int? 状态 = null,
        [FromQuery(Name = "期间编号")] int? 期间编号 = null)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 500);

        var 条件 = new List<string>();
        var ps = new List<(string 名, object? 值)>();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            条件.Add("(v.凭证字号 LIKE @kw OR ISNULL(v.摘要, N'') LIKE @kw)");
            ps.Add(("@kw", $"%{keyword.Trim()}%"));
        }
        if (from is DateTime 起)
        {
            条件.Add("v.凭证日期 >= @d1");
            ps.Add(("@d1", 起.Date));
        }
        if (to is DateTime 止)
        {
            条件.Add("v.凭证日期 <= @d2");
            ps.Add(("@d2", 止.Date));
        }
        if (状态 is int s)
        {
            条件.Add("v.单据状态 = @st");
            ps.Add(("@st", s));
        }
        if (期间编号 is > 0)
        {
            条件.Add("v.期间编号 = @per");
            ps.Add(("@per", 期间编号));
        }
        var where = 条件.Count > 0 ? "WHERE " + string.Join(" AND ", 条件) : "";

        var total = await _data.标量Async<long>(
            $"SELECT COUNT(1) FROM dbo.凭证 v {where}", ct, ps.ToArray());

        var sql = $"""
            SELECT v.凭证编号, v.凭证字号, v.凭证日期, v.期间编号,
                   ISNULL(p.期间名称, '')  AS 期间名称,
                   ISNULL(p.期间状态, 0)   AS 期间状态,
                   ISNULL(v.业务类型代码, '') AS 业务类型代码,
                   ISNULL(v.摘要, '')      AS 摘要,
                   v.借方合计, v.贷方合计, v.分录行数, v.单据状态,
                   v.是否冲销, v.原凭证编号, v.冲销凭证编号,
                   ISNULL(m.姓名, '')      AS 制单人,
                   ISNULL(a.姓名, '')      AS 过账人,
                   v.制单时间, v.过账时间,
                   CASE WHEN v.单据状态 = 2 AND ISNULL(p.期间状态, 0) = 0 AND v.是否冲销 = 0
                             AND NOT EXISTS (SELECT 1 FROM dbo.凭证 r
                                             WHERE r.原凭证编号 = v.凭证编号 AND r.是否冲销 = 1)
                        THEN 1 ELSE 0 END AS 可红冲,
                   CASE WHEN v.单据状态 = 0 AND ISNULL(p.期间状态, 0) = 0 THEN 1 ELSE 0 END AS 可过账,
                   CASE WHEN v.单据状态 <> 2 THEN N'只有已记账凭证可红冲'
                        WHEN v.是否冲销 = 1 THEN N'红冲凭证不再红冲'
                        WHEN ISNULL(p.期间状态, 0) = 1 THEN N'所属期间已结账（原凭证不可再改，需先反月结）'
                        WHEN EXISTS (SELECT 1 FROM dbo.凭证 r
                                     WHERE r.原凭证编号 = v.凭证编号 AND r.是否冲销 = 1)
                             THEN N'该凭证已红冲过'
                        ELSE N'' END AS 红冲受阻原因
            FROM dbo.凭证 v
            LEFT JOIN dbo.会计期间 p ON p.期间编号 = v.期间编号
            LEFT JOIN dbo.员工 m ON m.员工编号 = v.制单人编号
            LEFT JOIN dbo.员工 a ON a.员工编号 = v.过账人编号
            {where}
            ORDER BY v.凭证日期 DESC, v.凭证编号 DESC
            OFFSET {(page - 1) * size} ROWS FETCH NEXT {size} ROWS ONLY
            """;

        var items = await _data.查询SqlAsync(sql, ct, ps.ToArray());
        return Ok(new { total, items });
    }

    /// <summary>凭证详情：主表 + 全部分录（读 vw_凭证分录，科目/客户/商品名称都在里面）</summary>
    [HttpGet("vouchers/{id:int}")]
    [资源("Fin.Voucher")]
    [需要权限("view")]
    public async Task<IActionResult> 凭证详情(int id, CancellationToken ct)
    {
        var 主 = await _data.查询SqlAsync("""
            SELECT v.凭证编号, v.凭证字号, v.凭证日期, v.期间编号,
                   ISNULL(p.期间名称, '')   AS 期间名称,
                   ISNULL(p.期间状态, 0)    AS 期间状态,
                   ISNULL(v.业务类型代码, '') AS 业务类型代码,
                   ISNULL(v.摘要, '')       AS 摘要,
                   v.借方合计, v.贷方合计, v.分录行数, v.单据状态,
                   v.是否冲销, v.原凭证编号, v.冲销凭证编号,
                   ISNULL(m.姓名, '')       AS 制单人,
                   ISNULL(a.姓名, '')       AS 过账人,
                   v.制单时间, v.过账时间, ISNULL(v.备注, '') AS 备注
            FROM dbo.凭证 v
            LEFT JOIN dbo.会计期间 p ON p.期间编号 = v.期间编号
            LEFT JOIN dbo.员工 m ON m.员工编号 = v.制单人编号
            LEFT JOIN dbo.员工 a ON a.员工编号 = v.过账人编号
            WHERE v.凭证编号 = @id
            """, ct, ("@id", id));

        if (主.Count == 0) return NotFound(new { message = $"凭证 {id} 不存在" });

        var 分录 = await _data.查询SqlAsync("""
            SELECT 行号, ISNULL(分录摘要, '') AS 摘要, 科目编码, 科目名称, 科目类别,
                   借方金额, 贷方金额,
                   ISNULL(客户简称, '')   AS 客户,
                   ISNULL(供应商简称, '') AS 供应商,
                   ISNULL(员工, '')       AS 员工,
                   ISNULL(商品名称, '')   AS 商品,
                   数量, 单价
            FROM dbo.vw_凭证分录
            WHERE 凭证编号 = @id
            ORDER BY 行号
            """, ct, ("@id", id));

        return Ok(new { 主表 = 主[0], 分录 });
    }

    /// <summary>
    /// 新建手工凭证（借贷必须平）。
    ///
    /// 两步分开做，是为了让"谁报的错"清楚：
    ///   1. C# 先做**形状校验**（分录数、方向、金额、借贷合计）——这类错没必要惊动数据库；
    ///   2. 真正落库仍走 usp_财务_手工凭证 + usp_财务_加分录，一个事务里建完表头与全部分录，
    ///      任一行被过程拒绝（科目停用 54467、期间已结账 54441 …）整张凭证一起回滚。
    /// 借贷平衡用两位小数严格相等（与 凭证过账 的 0.01 容差同口径，但这里更严：
    /// 录入阶段就该一分不差，别把"差 0.01 也能过账"的习惯带进账里）。
    /// </summary>
    [HttpPost("vouchers")]
    [资源("Fin.Voucher")]
    [需要权限("add")]
    public async Task<IActionResult> 新建凭证([FromBody] 手工凭证入参 入参, CancellationToken ct)
    {
        var 分录 = 入参.分录 ?? new List<分录入参>();
        if (分录.Count < 2)
            return BadRequest(new { message = "手工凭证至少需要两条分录入（一借一贷）" });

        var 借 = 0m;
        var 贷 = 0m;
        for (var i = 0; i < 分录.Count; i++)
        {
            var l = 分录[i];
            var 方向 = (l.借贷方向 ?? "").Trim();
            if (方向 is not ("借" or "贷"))
                return BadRequest(new { message = $"第 {i + 1} 行：借贷方向只能是「借」或「贷」" });
            if (l.金额 is null || l.金额 <= 0)
                return BadRequest(new { message = $"第 {i + 1} 行：金额必须大于 0" });
            if (string.IsNullOrWhiteSpace(l.科目编码))
                return BadRequest(new { message = $"第 {i + 1} 行：请选择科目" });

            if (方向 == "借") 借 += l.金额!.Value;
            else 贷 += l.金额!.Value;
        }
        借 = Math.Round(借, 2, MidpointRounding.AwayFromZero);
        贷 = Math.Round(贷, 2, MidpointRounding.AwayFromZero);
        if (借 != 贷)
            return BadRequest(new { message = $"借贷不平：借 {借:N2} / 贷 {贷:N2}，差额 {借 - 贷:N2}" });

        var 日期 = 入参.凭证日期 is { } d ? d.Date : DateTime.Today;
        if (日期.Year < 2000 || 日期.Year > 2100)
            return BadRequest(new { message = "凭证日期不合法" });

        // 日期落在已结账期间：先说清楚，别让过程的 54441 变成一句看不懂的 500。
        var 已结账 = await _data.标量Async<int>("""
            SELECT COUNT(1) FROM dbo.会计期间
            WHERE 公司编号 = 1 AND 年度 = @y AND 月份 = @m AND 期间状态 = 1
            """, ct, ("@y", 日期.Year), ("@m", 日期.Month));
        if (已结账 > 0)
            return Conflict(new { message = $"{日期:yyyy-MM} 已结账，不能再录凭证（如需更正请在本期红冲/补录）" });

        var 公司 = await _data.标量Async<int?>(
            "SELECT TOP (1) 公司编号 FROM dbo.公司 WHERE 是否有效 = 1 ORDER BY 是否集团总部 DESC, 公司编号", ct);
        if (公司 is null) return Conflict(new { message = "没有可用的公司主体，无法建凭证" });

        // ⚠ JSON 的**键名必须是 ASCII**：SQL Server 的 JSON 路径解析器不认中文键
        //   （'$.科目编码' 直接报 13607「JSON 路径格式不正确」），加双引号也不行。
        //   所以键名一律用 k/d/a/m，中文只出现在**值**里（借/贷、摘要），那是数据不是路径。
        var 明细 = 分录.Select(l => new
        {
            k = (l.科目编码 ?? "").Trim(),
            d = (l.借贷方向 ?? "").Trim(),
            a = Math.Round(l.金额 ?? 0, 2, MidpointRounding.AwayFromZero),
            m = string.IsNullOrWhiteSpace(l.摘要) ? 入参.摘要 : l.摘要
        }).ToList();

        try
        {
            var 行 = new List<Dictionary<string, object?>>();
            await using (var tx = await _db.Database.BeginTransactionAsync(ct))
            {
                var vc = await _data.查询SqlAsync("""
                    CALL public."usp_财务_手工凭证"(@comp,@d,@memo,@emp,@v)
                    """, ct,
                    ("@comp", 公司.Value), ("@d", 日期),
                    ("@memo", string.IsNullOrWhiteSpace(入参.摘要) ? "手工凭证" : 入参.摘要!.Trim()),
                    ("@emp", _me.员工编号 == 0 ? (object?)null : _me.员工编号), ("@v", null));
                var _vr = vc.FirstOrDefault();
                var v = _vr is not null && _vr.TryGetValue("p_凭证编号", out var _vv) && _vv is not null ? Convert.ToInt32(_vv) : 0;
                foreach (var l in 明细)
                {
                    await _data.查询SqlAsync("""
                        CALL public."usp_财务_加分录"(@v,@k,@dir,@amt,@line,NULL,NULL,NULL,NULL,@lineId)
                        """, ct,
                        ("@v", v), ("@k", l.k), ("@dir", l.d), ("@amt", l.a), ("@line", l.m), ("@lineId", null));
                }
                var tot = await _data.查询SqlAsync("""
                    SELECT @v AS "凭证编号",
                           (SELECT "凭证字号" FROM "凭证" WHERE "凭证编号" = @v) AS "凭证字号",
                           (SELECT "期间编号" FROM "凭证" WHERE "凭证编号" = @v) AS "期间编号",
                           (SELECT COALESCE(public.fn_round(SUM(CAST("借方金额" AS numeric(38,6))), 2), 0) FROM "凭证分录" WHERE "凭证编号" = @v) AS "借方合计",
                           (SELECT COALESCE(public.fn_round(SUM(CAST("贷方金额" AS numeric(38,6))), 2), 0) FROM "凭证分录" WHERE "凭证编号" = @v) AS "贷方合计";
                    """, ct, ("@v", v));
                await tx.CommitAsync(ct);
                行 = tot;
            }

            if (行.Count == 0) return StatusCode(500, new { message = "凭证已提交但没有返回结果，请刷新台账确认" });
            return Ok(new
            {
                凭证编号 = 行[0]["凭证编号"],
                凭证字号 = 行[0]["凭证字号"],
                期间编号 = 行[0]["期间编号"],
                借方合计 = 行[0]["借方合计"],
                贷方合计 = 行[0]["贷方合计"]
            });
        }
        catch (Exception ex)
        {
            return 业务错误(ex);
        }
    }

    /// <summary>过账（草稿 → 已记账）。借贷不平/无分录由 usp_财务_凭证过账 拦。</summary>
    [HttpPost("vouchers/{id:int}/post")]
    [资源("Fin.Voucher")]
    [需要权限("post")]
    public async Task<IActionResult> 凭证过账(int id, CancellationToken ct)
    {
        try
        {
            await _data.查询SqlAsync("CALL public.\"usp_财务_凭证过账\"(@id,@emp)", ct,
                ("@id", id), ("@emp", _me.员工编号 == 0 ? (object?)null : _me.员工编号));
            var 行 = await _data.查询SqlAsync("""
                SELECT "凭证编号", "凭证字号", "借方合计", "贷方合计", "分录行数", "单据状态", "过账时间"
                FROM "凭证" WHERE "凭证编号" = @id;
                """, ct, ("@id", id));

            if (行.Count == 0) return NotFound(new { message = $"凭证 {id} 不存在" });
            return Ok(new { ok = true, 凭证 = 行[0] });
        }
        catch (Exception ex)
        {
            return 业务错误(ex);
        }
    }

    /// <summary>
    /// 红冲：生成一张借贷对调的红字凭证（是否冲销=1，原凭证编号 指向原凭证），原凭证置为已作废(9)。
    /// 只对「已记账且期间未结账」的凭证合法 —— 原因见 <see cref="凭证列表"/> 的注释。
    /// 返回新凭证编号，前端据此跳过去看。
    /// </summary>
    [HttpPost("vouchers/{id:int}/reverse")]
    [资源("Fin.Voucher")]
    [需要权限("post")]
    public async Task<IActionResult> 凭证红冲(int id, [FromBody] 红冲入参? 入参, CancellationToken ct)
    {
        try
        {
            var call = await _data.查询SqlAsync("CALL public.\"usp_财务_红冲\"(@id,@emp,@why,@new)", ct,
                ("@id", id),
                ("@emp", _me.员工编号 == 0 ? (object?)null : _me.员工编号),
                ("@why", string.IsNullOrWhiteSpace(入参?.原因) ? "红冲" : 入参!.原因!.Trim()),
                ("@new", null));
            var _cr = call.FirstOrDefault();
            var nv = _cr is not null && _cr.TryGetValue("p_新凭证编号", out var _vv) && _vv is not null ? Convert.ToInt32(_vv) : 0;
            var 行 = await _data.查询SqlAsync("""
                SELECT @new AS "新凭证编号",
                       (SELECT "凭证字号" FROM "凭证" WHERE "凭证编号" = @new) AS "新凭证字号",
                       (SELECT "凭证日期" FROM "凭证" WHERE "凭证编号" = @new) AS "新凭证日期",
                       (SELECT COALESCE("摘要", '') FROM "凭证" WHERE "凭证编号" = @new) AS "摘要";
                """, ct, ("@new", nv));

            if (行.Count == 0) return StatusCode(500, new { message = "红冲没有返回新凭证，请刷新台账确认" });
            return Ok(new { ok = true, 新凭证编号 = 行[0]["新凭证编号"], 新凭证字号 = 行[0]["新凭证字号"], 摘要 = 行[0]["摘要"] });
        }
        catch (Exception ex)
        {
            return 业务错误(ex);
        }
    }

    /// <summary>科目下拉（只给"启用 + 明细"科目：汇总科目不能直接记账）</summary>
    [HttpGet("subjects")]
    [资源("Fin.Voucher")]
    [需要权限("view")]
    public async Task<IActionResult> 科目(CancellationToken ct)
    {
        var rows = await _data.查询SqlAsync("""
            SELECT 科目编号, 科目编码, 科目名称, 科目类别,
                   是否损益科目, ISNULL(借贷方向, '') AS 借贷方向
            FROM dbo.会计科目
            WHERE 是否停用 = 0 AND 是否明细 = 1
            ORDER BY 科目编码
            """, ct);
        return Ok(rows);
    }

    /* ===================================================================== */
    /* 期间管理                                                              */
    /* ===================================================================== */

    /// <summary>
    /// 会计期间列表 + 四个动作的合法性（前端据此只渲染合法按钮）。
    ///
    /// 合法性口径全抄自库里，不另立一套：
    ///   可月结   = 期间状态 0（未结账）且 本期**已记账**凭证借贷平衡（usp_财务_月结 的硬门槛 54488）
    ///   可反月结 = 期间状态 1 且 没有更晚的已结账期间（usp_财务_反月结 的 54491）
    ///   可结转损益 / 可成本结转 = 期间状态 0（两个过程都在已结账期间 THROW 54482 / 54484）
    /// 另外给出 草稿张数：月结不要求草稿先过账，但期间一锁草稿就再也过不了账，
    /// 所以把它当**警示**显示（不拦按钮 —— 库里没这条门槛，我们不替它加戏）。
    /// </summary>
    [HttpGet("periods")]
    [资源("Fin.Period")]
    [需要权限("view")]
    public async Task<IActionResult> 期间列表(CancellationToken ct)
    {
        var rows = await _data.查询SqlAsync("""
            WITH 凭 AS (
                SELECT 期间编号,
                       SUM(CASE WHEN 单据状态 = 2 THEN 1 ELSE 0 END) AS 已记账张数,
                       SUM(CASE WHEN 单据状态 = 0 THEN 1 ELSE 0 END) AS 草稿张数,
                       ISNULL(dbo.fn_round(SUM(CASE WHEN 单据状态 = 2
                                                   THEN CAST(借方合计 AS decimal(38,6)) ELSE 0 END), 2), 0) AS 借,
                       ISNULL(dbo.fn_round(SUM(CASE WHEN 单据状态 = 2
                                                   THEN CAST(贷方合计 AS decimal(38,6)) ELSE 0 END), 2), 0) AS 贷
                FROM dbo.凭证 GROUP BY 期间编号
            ),
            成本 AS (
                SELECT 期间编号, COUNT(1) AS 行数,
                       SUM(CASE WHEN 是否已结账 = 1 THEN 1 ELSE 0 END) AS 已结账行数
                FROM dbo.成本结转记录 GROUP BY 期间编号
            )
            SELECT p.期间编号, p.年度, p.月份, p.期间名称, p.开始日期, p.结束日期, p.期间状态,
                   p.是否结转损益,
                   ISNULL(e.姓名, '') AS 结账人, p.结账时间,
                   p.凭证张数 AS 台账张数, p.借方合计 AS 台账借方, p.贷方合计 AS 台账贷方,
                   ISNULL(x.已记账张数, 0) AS 已记账张数,
                   ISNULL(x.草稿张数, 0)   AS 草稿张数,
                   ISNULL(x.借, 0)         AS 已记账借方,
                   ISNULL(x.贷, 0)         AS 已记账贷方,
                   ISNULL(x.借, 0) - ISNULL(x.贷, 0) AS 借贷差,
                   ISNULL(c.行数, 0)       AS 成本结转行数,
                   ISNULL(c.已结账行数, 0) AS 成本结转已结账行数,
                   (SELECT COUNT(1) FROM dbo.会计期间 q
                     WHERE q.期间状态 = 1 AND q.结束日期 > p.结束日期) AS 后续已结账数,
                   CASE WHEN p.期间状态 = 0
                             AND ABS(ISNULL(x.借, 0) - ISNULL(x.贷, 0)) <= 0.01 THEN 1 ELSE 0 END AS 可月结,
                   CASE WHEN p.期间状态 = 1
                             AND NOT EXISTS (SELECT 1 FROM dbo.会计期间 q
                                             WHERE q.期间状态 = 1 AND q.结束日期 > p.结束日期)
                        THEN 1 ELSE 0 END AS 可反月结,
                   CASE WHEN p.期间状态 = 0 THEN 1 ELSE 0 END AS 可结转,
                   CASE WHEN p.期间状态 = 1 THEN N'已结账'
                        WHEN ABS(ISNULL(x.借, 0) - ISNULL(x.贷, 0)) > 0.01
                             THEN N'本期已记账凭证借贷不平（差 ' + CAST(CAST(ISNULL(x.借,0) - ISNULL(x.贷,0) AS decimal(19,2)) AS nvarchar(30)) + N'）'
                        ELSE N'' END AS 月结受阻原因
            FROM dbo.会计期间 p
            LEFT JOIN dbo.员工 e ON e.员工编号 = p.结账人编号
            LEFT JOIN 凭 x ON x.期间编号 = p.期间编号
            LEFT JOIN 成本 c ON c.期间编号 = p.期间编号
            ORDER BY p.年度 DESC, p.月份 DESC
            """, ct);

        var 已结账数 = rows.Count(r => Convert.ToInt32(r["期间状态"]) == 1);
        return Ok(new
        {
            合计 = rows.Count,
            已结账数,
            未结账数 = rows.Count - 已结账数,
            items = rows
        });
    }

    /// <summary>月结：结转成本 → 结转损益 → 试算平衡 → 锁账（usp_财务_月结 一把做完）</summary>
    [HttpPost("periods/{id:int}/close")]
    [资源("Fin.Period")]
    [需要权限("audit")]
    public async Task<IActionResult> 月结(int id, CancellationToken ct)
    {
        try
        {
            await _data.查询SqlAsync("CALL public.\"usp_财务_月结\"(@id,@emp,NULL)", ct,
                ("@id", id), ("@emp", _me.员工编号 == 0 ? (object?)null : _me.员工编号));
            var 行 = await _data.查询SqlAsync("""
                SELECT (SELECT "凭证编号" FROM "凭证"
                         WHERE "期间编号" = @id AND "业务类型代码" = 'CLOSE_PL'
                         ORDER BY "凭证编号" DESC LIMIT 1) AS "结转损益凭证编号",
                       (SELECT "凭证字号" FROM "凭证"
                         WHERE "期间编号" = @id AND "业务类型代码" = 'CLOSE_PL'
                         ORDER BY "凭证编号" DESC LIMIT 1) AS "结转损益凭证字号",
                       (SELECT "期间状态" FROM "会计期间" WHERE "期间编号" = @id) AS "期间状态",
                       (SELECT "凭证张数" FROM "会计期间" WHERE "期间编号" = @id) AS "凭证张数";
                """, ct, ("@id", id));

            return Ok(new
            {
                ok = true,
                动作 = "月结",
                结转损益凭证编号 = 行.Count > 0 ? 行[0]["结转损益凭证编号"] : null,
                结转损益凭证字号 = 行.Count > 0 ? 行[0]["结转损益凭证字号"] : null,
                期间状态 = 行.Count > 0 ? 行[0]["期间状态"] : null
            });
        }
        catch (Exception ex)
        {
            return 业务错误(ex);
        }
    }

    /// <summary>反月结：只允许最后一个已结账期间；结转凭证不删（留痕），期间可再结</summary>
    [HttpPost("periods/{id:int}/reopen")]
    [资源("Fin.Period")]
    [需要权限("audit")]
    public async Task<IActionResult> 反月结(int id, CancellationToken ct)
    {
        try
        {
            await _data.查询SqlAsync("CALL public.\"usp_财务_反月结\"(@id,@emp)", ct,
                ("@id", id), ("@emp", _me.员工编号 == 0 ? (object?)null : _me.员工编号));
            var 行 = await _data.查询SqlAsync("""
                SELECT "期间编号", "期间状态", "结账人编号", "结账时间" FROM "会计期间" WHERE "期间编号" = @id;
                """, ct, ("@id", id));

            return Ok(new { ok = true, 动作 = "反月结", 期间 = 行.Count > 0 ? 行[0] : null });
        }
        catch (Exception ex)
        {
            return 业务错误(ex);
        }
    }

    /// <summary>结转损益（单独执行；月结内部也会调，重复执行是幂等的：余额为 0 就不生成）</summary>
    [HttpPost("periods/{id:int}/close-pl")]
    [资源("Fin.Period")]
    [需要权限("post")]
    public async Task<IActionResult> 结转损益(int id, CancellationToken ct)
    {
        try
        {
            var call = await _data.查询SqlAsync("CALL public.\"usp_财务_结转损益\"(@id,@emp,@v)", ct,
                ("@id", id), ("@emp", _me.员工编号 == 0 ? (object?)null : _me.员工编号), ("@v", null));
            var _vr = call.FirstOrDefault();
            object? vv = _vr is not null && _vr.TryGetValue("p_凭证编号", out var _v0) ? _v0 : null;
            var 行 = await _data.查询SqlAsync("""
                SELECT @v::integer AS "凭证编号",
                       (SELECT COALESCE("凭证字号", '') FROM "凭证" WHERE "凭证编号" = @v::integer) AS "凭证字号",
                       (SELECT COALESCE("摘要", '') FROM "凭证" WHERE "凭证编号" = @v::integer) AS "摘要";
                """, ct, ("@v", vv));

            var 凭证编号 = 行.Count > 0 ? 行[0]["凭证编号"] : null;
            return Ok(new
            {
                ok = true,
                动作 = "结转损益",
                凭证编号,
                // 过程在本期损益余额为 0 时直接 RETURN（不生成凭证），这不是失败，要说清楚
                说明 = 凭证编号 is null || Convert.ToString(凭证编号) == "" ? "本期损益余额为 0，未生成结转凭证" : "已生成结转损益凭证"
            });
        }
        catch (Exception ex)
        {
            return 业务错误(ex);
        }
    }

    /// <summary>成本结转（按期间重算移动加权成本台账，幂等覆盖）</summary>
    [HttpPost("periods/{id:int}/close-cost")]
    [资源("Fin.Period")]
    [需要权限("post")]
    public async Task<IActionResult> 成本结转(int id, CancellationToken ct)
    {
        try
        {
            var 期 = await _data.查询SqlAsync("""
                SELECT "公司编号", "年度", "月份" FROM "会计期间" WHERE "期间编号" = @id;
                """, ct, ("@id", id));
            var _pr = 期.FirstOrDefault();
            var comp = _pr is not null && _pr.TryGetValue("公司编号", out var _c1) && _c1 is not null ? Convert.ToInt32(_c1) : 0;
            var yr = _pr is not null && _pr.TryGetValue("年度", out var _c2) && _c2 is not null ? Convert.ToInt16(_c2) : (short)0;
            var mo = _pr is not null && _pr.TryGetValue("月份", out var _c3) && _c3 is not null ? Convert.ToInt16(_c3) : (short)0;
            var call = await _data.查询SqlAsync("CALL public.\"usp_财务_成本结转\"(@comp::integer,@yr::smallint,@mo::smallint,@id::integer,@emp::integer,@rows)", ct,
                ("@comp", comp), ("@yr", yr), ("@mo", mo), ("@id", id),
                ("@emp", _me.员工编号 == 0 ? (object?)null : _me.员工编号), ("@rows", null));
            var _rr = call.FirstOrDefault();
            object? rows = _rr is not null && _rr.TryGetValue("p_结转行数", out var _r0) ? _r0 : null;
            var 行 = await _data.查询SqlAsync("""
                SELECT @rows::integer AS "结转行数",
                       (SELECT COUNT(1) FROM "成本结转记录" WHERE "期间编号" = @id) AS "台账行数";
                """, ct, ("@rows", rows), ("@id", id));

            return Ok(new
            {
                ok = true,
                动作 = "成本结转",
                结转行数 = 行.Count > 0 ? 行[0]["结转行数"] : 0,
                台账行数 = 行.Count > 0 ? 行[0]["台账行数"] : 0
            });
        }
        catch (Exception ex)
        {
            return 业务错误(ex);
        }
    }

    /* ===================================================================== */
    /* 错误翻译                                                              */
    /* ===================================================================== */

    /// <summary>
    /// 把库里的 THROW 544xx（业务规则）翻成 409 + 原文，其余当 500。
    ///
    /// 为什么要分段：54400~54499 是本项目所有过程/触发器的业务错误号段（见 db/v3），
    /// 这些消息本来就是写给操作员看的中文（"本期凭证借贷不平，不能月结"），
    /// 直接透传比在这里重写一遍更准 —— 重写就多了一份会过期的口径。
    /// </summary>
    private IActionResult 业务错误(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is PostgresException pe && pe.SqlState == "P0001")
                return Conflict(new { message = pe.MessageText, code = 54400 });
        }
        _log.LogError(ex, "财务操作失败");
        return StatusCode(500, new { message = "操作失败：" + ex.Message });
    }
}

/* ======================================================================= */
/* 入参                                                                     */
/* ======================================================================= */

/// <summary>手工凭证一行分录</summary>
public sealed class 分录入参
{
    public string? 科目编码 { get; set; }
    public string? 借贷方向 { get; set; }
    public decimal? 金额 { get; set; }
    public string? 摘要 { get; set; }
}

/// <summary>手工凭证表头 + 分录</summary>
public sealed class 手工凭证入参
{
    public DateTime? 凭证日期 { get; set; }
    public string? 摘要 { get; set; }
    public List<分录入参>? 分录 { get; set; }
}

/// <summary>红冲原因（可空，过程里会给默认值）</summary>
public sealed class 红冲入参
{
    public string? 原因 { get; set; }
}
