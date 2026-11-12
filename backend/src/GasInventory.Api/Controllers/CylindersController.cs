using GasInventory.Api.Auth;
using GasInventory.Api.Services;
using GasInventory.Infrastructure.Data;
using GasInventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Microsoft.EntityFrameworkCore;

namespace GasInventory.Api.Controllers;

/// <summary>
/// 钢瓶一物一码作业面（批次 18）。
///
/// 口径（以库为准，页面不要另算）：
///   · 一只瓶 = 一条 钢瓶档案（钢瓶号码唯一），位置/状态/轨迹由 usp_钢瓶_* 过程维护；
///   · 建档、检验、报废、遗失、移动全部调过程，不直插档案、不改历史检验记录；
///   · **钢瓶资产不是库存数量**：这些过程只写 钢瓶档案 / 钢瓶流转明细 / 钢瓶产权变更，
///     不写 库存结存、库存流水、凭证。按数量的仓库间转移走库存调拨（批次 19），
///     两者是"一物一码"与"数量结存"两套口径。
/// </summary>
[ApiController]
[Route("api/cylinders")]
[Authorize]
[资源("Cyl.Archive")]
public class CylindersController : ControllerBase
{
    private static readonly string[] 允许状态 = { "在库", "在途", "在用", "借出", "报检", "报废", "遗失" };
    private static readonly string[] 允许报废原因 = { "检验报废", "腐蚀损坏", "事故损坏", "超期", "其他" };
    private static readonly string[] 允许赔偿方式 = { "现金", "押金抵扣", "免赔", "换瓶" };

    private readonly GasInventoryDbContext _db;
    private readonly 数据服务 _data;
    private readonly 当前登录 _me;

    public CylindersController(GasInventoryDbContext db, 数据服务 data, 当前登录 me)
    {
        _db = db;
        _data = data;
        _me = me;
    }

    // ---------------- 查询 ----------------

    /// <summary>钢瓶台账分页 + 全局状态统计（统计不受筛选影响，给页面顶部卡片用）。</summary>
    [HttpGet]
    [需要权限("VIEW")]
    public async Task<IActionResult> 列表(
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "size")] int size = 50,
        [FromQuery(Name = "keyword")] string? keyword = null,
        [FromQuery(Name = "状态")] string? 状态 = null,
        [FromQuery(Name = "仓库编号")] int? 仓库编号 = null,
        [FromQuery(Name = "逾期")] bool? 逾期 = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 500);

        var 条件 = new List<string> { "1 = 1" };
        var ps = new List<(string 名, object? 值)>();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            条件.Add("""
                (b.钢瓶号码 LIKE @kw OR ISNULL(b.条码, '') LIKE @kw
                 OR p.商品名称 LIKE @kw OR ISNULL(k.客户简称, '') LIKE @kw
                 OR ISNULL(w.仓库名称, '') LIKE @kw)
                """);
            ps.Add(("@kw", $"%{keyword.Trim()}%"));
        }
        if (!string.IsNullOrWhiteSpace(状态))
        {
            var s = 状态.Trim();
            if (!允许状态.Contains(s))
                return BadRequest(new { message = $"未知钢瓶状态：{s}" });
            条件.Add("b.状态 = @state");
            ps.Add(("@state", s));
        }
        if (仓库编号 is > 0)
        {
            条件.Add("b.当前仓库编号 = @wh");
            ps.Add(("@wh", 仓库编号.Value));
        }
        if (逾期 == true)
        {
            条件.Add("b.状态 NOT IN (N'报废', N'遗失') AND b.下次检验日期 IS NOT NULL AND b.下次检验日期 < CAST(GETDATE() AS date)");
        }

        var where = "WHERE " + string.Join(" AND ", 条件);
        var total = await _data.标量Async<long>(
            $"""
             SELECT COUNT(1)
             FROM dbo.钢瓶档案 b
             JOIN dbo.商品 p ON p.商品编号 = b.商品编号
             LEFT JOIN dbo.仓库 w ON w.仓库编号 = b.当前仓库编号
             LEFT JOIN dbo.客户 k ON k.客户编号 = b.当前客户编号
             {where}
             """, ct, ps.ToArray());

        var sql = $"""
            SELECT b.钢瓶编号, b.钢瓶号码, b.商品编号, p.商品编码, p.商品名称, a.瓶型,
                   b.产权归属, b.状态, b.当前仓库编号, w.仓库名称,
                   b.当前客户编号, k.客户简称, b.当前车辆编号, v.车牌,
                   b.上次检验日期, b.下次检验日期,
                   DATEDIFF(day, CAST(GETDATE() AS date), b.下次检验日期) AS 检验剩余天数,
                   CAST(CASE WHEN b.状态 NOT IN (N'报废', N'遗失')
                                  AND b.下次检验日期 IS NOT NULL
                                  AND b.下次检验日期 < CAST(GETDATE() AS date)
                             THEN 1 ELSE 0 END AS bit) AS 是否逾期未检,
                   b.条码, b.生产厂家, b.生产日期, b.实际自重公斤, b.最大充装量公斤,
                   b.报废日期, b.报废原因, b.最后流转时间
            FROM dbo.钢瓶档案 b
            JOIN dbo.商品 p ON p.商品编号 = b.商品编号
            LEFT JOIN dbo.商品气瓶属性 a ON a.商品编号 = b.商品编号
            LEFT JOIN dbo.仓库 w ON w.仓库编号 = b.当前仓库编号
            LEFT JOIN dbo.客户 k ON k.客户编号 = b.当前客户编号
            LEFT JOIN dbo.车辆 v ON v.车辆编号 = b.当前车辆编号
            {where}
            ORDER BY b.钢瓶编号 DESC
            OFFSET {(page - 1) * size} ROWS FETCH NEXT {size} ROWS ONLY
            """;

        var items = await _data.查询SqlAsync(sql, ct, ps.ToArray());
        var 统计 = (await _data.查询SqlAsync("""
            SELECT COUNT(1) AS 总数,
                   SUM(CASE WHEN 状态 = N'在库' THEN 1 ELSE 0 END) AS 在库,
                   SUM(CASE WHEN 状态 = N'在途' THEN 1 ELSE 0 END) AS 在途,
                   SUM(CASE WHEN 状态 = N'在用' THEN 1 ELSE 0 END) AS 在用,
                   SUM(CASE WHEN 状态 = N'借出' THEN 1 ELSE 0 END) AS 借出,
                   SUM(CASE WHEN 状态 = N'报检' THEN 1 ELSE 0 END) AS 报检,
                   SUM(CASE WHEN 状态 = N'报废' THEN 1 ELSE 0 END) AS 报废,
                   SUM(CASE WHEN 状态 = N'遗失' THEN 1 ELSE 0 END) AS 遗失,
                   SUM(CASE WHEN 状态 NOT IN (N'报废', N'遗失')
                                  AND 下次检验日期 IS NOT NULL
                                  AND 下次检验日期 < CAST(GETDATE() AS date)
                            THEN 1 ELSE 0 END) AS 逾期未检
            FROM dbo.钢瓶档案
            """, ct)).FirstOrDefault() ?? new Dictionary<string, object?>();

        return Ok(new { total, items, 统计 });
    }

    /// <summary>
    /// 自检用：报出这个接口连的是哪个库。
    /// 并行批次会各自起副本 API（10375 / 10376 / 10377…），探针若打错端口就会把数据写进别人的库；
    /// 批次 18 探针开头必须先比对库名，才允许继续写。
    /// </summary>
    [HttpGet("db")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 库名(CancellationToken ct)
    {
        var r = await _data.查询SqlAsync("""
            SELECT DB_NAME() AS 库, @@SERVICENAME AS 实例,
                   CAST(SERVERPROPERTY('ProductVersion') AS varchar(30)) AS 版本
            """, ct);
        return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?> { ["库"] = null });
    }

    /// <summary>单只钢瓶详情：档案 + 只读检验历史 + 只增不改的流转轨迹。</summary>
    [HttpGet("{id:int}")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 详情(int id, CancellationToken ct)
    {
        var 档案 = await _data.查询SqlAsync("""
            SELECT b.钢瓶编号, b.钢瓶号码, b.商品编号, p.商品编码, p.商品名称, a.瓶型,
                   b.产权归属, b.状态, b.当前仓库编号, w.仓库名称,
                   b.当前客户编号, k.客户简称, b.当前车辆编号, v.车牌,
                   b.生产厂家, b.出厂日期, b.生产日期, b.生产批次,
                   b.设计自重公斤, b.实际自重公斤, b.最大充装量公斤,
                   b.上次检验日期, b.下次检验日期, b.检验周期月, b.使用年限,
                   b.报废日期, b.报废原因, b.条码, b.备注, b.最后流转时间,
                   DATEDIFF(day, CAST(GETDATE() AS date), b.下次检验日期) AS 检验剩余天数,
                   CAST(CASE WHEN b.状态 NOT IN (N'报废', N'遗失')
                                  AND b.下次检验日期 IS NOT NULL
                                  AND b.下次检验日期 < CAST(GETDATE() AS date)
                             THEN 1 ELSE 0 END AS bit) AS 是否逾期未检
            FROM dbo.钢瓶档案 b
            JOIN dbo.商品 p ON p.商品编号 = b.商品编号
            LEFT JOIN dbo.商品气瓶属性 a ON a.商品编号 = b.商品编号
            LEFT JOIN dbo.仓库 w ON w.仓库编号 = b.当前仓库编号
            LEFT JOIN dbo.客户 k ON k.客户编号 = b.当前客户编号
            LEFT JOIN dbo.车辆 v ON v.车辆编号 = b.当前车辆编号
            WHERE b.钢瓶编号 = @id
            """, ct, ("@id", id));
        if (档案.Count == 0) return NotFound(new { message = "钢瓶不存在" });

        var 检验记录 = await _data.查询SqlAsync("""
            SELECT 检验记录编号, 检验日期, 检验类型, 检验机构, 检验结论, 下次检验日期,
                   检验报告号, 检验费用, 备注, 创建时间
            FROM dbo.钢瓶检验记录
            WHERE 钢瓶编号 = @id
            ORDER BY 检验日期 DESC, 检验记录编号 DESC
            """, ct, ("@id", id));

        var 轨迹 = await _data.查询SqlAsync("""
            SELECT 流转编号, 发生时间, 业务日期, 业务类型,
                   来源仓库, 去向仓库, 来源客户, 去向客户,
                   单据类型代码, 单据编号, 备注
            FROM dbo.vw_钢瓶轨迹
            WHERE 钢瓶编号 = @id
            ORDER BY 流转编号 DESC
            """, ct, ("@id", id));

        return Ok(new { 档案 = 档案[0], 检验记录, 轨迹 });
    }

    // ---------------- 建档 ----------------

    public sealed class 建档行
    {
        public string? 钢瓶号码 { get; set; }
        public string? 条码 { get; set; }
        public DateTime? 生产日期 { get; set; }
        public string? 生产批次 { get; set; }
    }

    public sealed class 批量建档入参
    {
        public int 商品编号 { get; set; }
        public string? 产权归属 { get; set; }
        public int? 客户编号 { get; set; }
        public int? 供应商编号 { get; set; }
        public string? 生产厂家 { get; set; }
        public DateTime? 生产日期 { get; set; }
        public string? 生产批次 { get; set; }
        public decimal? 实际自重公斤 { get; set; }
        public decimal? 最大充装量公斤 { get; set; }
        public int? 当前仓库编号 { get; set; }
        public List<建档行> 明细 { get; set; } = new();
    }

    /// <summary>
    /// 批量建档：一次录 N 只，逐只调 usp_钢瓶_建档（号码唯一、首条轨迹、商品必须是气品）。
    /// 整批一个事务：第 3 只撞重复号时，前 2 只也不会留下半批数据。
    /// </summary>
    [HttpPost("batch")]
    [需要权限("ADD")]
    public async Task<IActionResult> 批量建档([FromBody] 批量建档入参 body, CancellationToken ct)
    {
        if (body.商品编号 <= 0) return BadRequest(new { message = "请选择商品" });

        var 产权 = string.IsNullOrWhiteSpace(body.产权归属) ? "自有" : body.产权归属.Trim();
        if (产权 is not ("自有" or "客户" or "供应商"))
            return BadRequest(new { message = "产权归属只能是 自有 / 客户 / 供应商" });
        if (产权 == "客户" && (body.客户编号 is null or <= 0))
            return BadRequest(new { message = "产权归属为客户时必须选择客户" });
        if (产权 == "供应商" && (body.供应商编号 is null or <= 0))
            return BadRequest(new { message = "产权归属为供应商时必须选择供应商" });

        var 行 = (body.明细 ?? new List<建档行>())
            .Where(x => !string.IsNullOrWhiteSpace(x.钢瓶号码))
            .Select(x => new
            {
                x.钢瓶号码,
                x.条码,
                生产日期 = x.生产日期 ?? body.生产日期,
                生产批次 = string.IsNullOrWhiteSpace(x.生产批次) ? body.生产批次 : x.生产批次
            })
            .ToList();
        if (行.Count == 0)
            return BadRequest(new { message = "至少填写一只钢瓶号码（一物一码，每行一个号）" });
        if (行.Count > 500)
            return BadRequest(new { message = "单批最多 500 只，请拆批建档" });

        var 号码集 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in 行)
        {
            var 号 = x.钢瓶号码!.Trim();
            if (!号码集.Add(号))
                return BadRequest(new { message = $"本批钢瓶号码重复：{号}" });
        }

        var 商品 = await _data.查询SqlAsync(
            "SELECT 是否气品 FROM dbo.商品 WHERE 商品编号 = @p", ct, ("@p", body.商品编号));
        if (商品.Count == 0) return BadRequest(new { message = "商品不存在" });
        if (!Convert.ToBoolean(商品[0]["是否气品"]))
            return BadRequest(new { message = "只能给气品商品建钢瓶档案" });

        if (body.当前仓库编号 is int wh)
        {
            var 仓 = await _data.查询SqlAsync(
                "SELECT 是否有效, 是否车仓 FROM dbo.仓库 WHERE 仓库编号 = @w", ct, ("@w", wh));
            if (仓.Count == 0) return BadRequest(new { message = "仓库不存在" });
            if (Convert.ToBoolean(仓[0]["是否车仓"]))
                return BadRequest(new { message = "建档入库不能选车仓；车仓请通过装车/配送进入" });
            if (!Convert.ToBoolean(仓[0]["是否有效"]))
                return BadRequest(new { message = "仓库已停用" });
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var 已建 = new List<Dictionary<string, object?>>();
            foreach (var x in 行)
            {
                var cr = await _data.查询SqlAsync("""
                    CALL public."usp_钢瓶_建档"(@no,@p,@bc,@own,@sup,@cust,@maker,@pd,@batch,@w,@cap,@wh,@emp,@id)
                    """, ct,
                    ("@no", x.钢瓶号码!.Trim()), ("@p", body.商品编号), ("@bc", x.条码),
                    ("@own", 产权), ("@sup", 产权 == "供应商" ? body.供应商编号 : null),
                    ("@cust", 产权 == "客户" ? body.客户编号 : null),
                    ("@maker", body.生产厂家), ("@pd", x.生产日期), ("@batch", x.生产批次),
                    ("@w", body.实际自重公斤), ("@cap", body.最大充装量公斤),
                    ("@wh", body.当前仓库编号), ("@emp", _me.员工编号), ("@id", null));
                var _cr = cr.FirstOrDefault();
                var newId = _cr is not null && _cr.TryGetValue("p_钢瓶编号", out var _cv) && _cv is not null ? Convert.ToInt32(_cv) : 0;
                已建.Add(new Dictionary<string, object?> { ["钢瓶编号"] = newId, ["钢瓶号码"] = x.钢瓶号码!.Trim(), ["条码"] = x.条码 });
            }
            await tx.CommitAsync(ct);
            return Ok(new { ok = true, 数量 = 已建.Count, 钢瓶 = 已建 });
        }
        catch (NpgsqlException ex)
        {
            await tx.RollbackAsync(ct);
            return Conflict(new { message = ex.Message });
        }
    }

    // ---------------- 检验 / 报废 / 遗失 / 移动 ----------------

    public sealed class 检验入参
    {
        public DateTime? 检验日期 { get; set; }
        public string? 检验类型 { get; set; }
        public string? 检验机构 { get; set; }
        public string? 检验结论 { get; set; }
        public DateTime? 下次检验日期 { get; set; }
        public string? 检验报告号 { get; set; }
        public decimal? 检验费用 { get; set; }
        public string? 备注 { get; set; }
    }

    /// <summary>新增一次检验（只增不改）。合格回填下次检验日期；不合格由过程自动转「报检」。</summary>
    [HttpPost("{id:int}/inspect")]
    [资源("Cyl.Inspect")]
    [需要权限("ADD")]
    public async Task<IActionResult> 检验(int id, [FromBody] 检验入参 body, CancellationToken ct)
    {
        var 档 = await _data.查询SqlAsync(
            "SELECT 状态 FROM dbo.钢瓶档案 WHERE 钢瓶编号 = @id", ct, ("@id", id));
        if (档.Count == 0) return NotFound(new { message = "钢瓶不存在" });
        var 状态 = Convert.ToString(档[0]["状态"]);
        if (状态 is "报废" or "遗失")
            return Conflict(new { message = $"钢瓶当前状态为「{状态}」，不能再做检验" });

        var 类型 = string.IsNullOrWhiteSpace(body.检验类型) ? "定期检验" : body.检验类型.Trim();
        if (类型 is not ("定期检验" or "水压试验" or "气密试验" or "外观检查" or "首次检验"))
            return BadRequest(new { message = "检验类型不合法" });
        if (string.IsNullOrWhiteSpace(body.检验机构))
            return BadRequest(new { message = "请填写检验机构" });
        var 结论 = body.检验结论?.Trim();
        if (结论 is not ("合格" or "不合格"))
            return BadRequest(new { message = "检验结论只能选 合格 / 不合格（判废请走「报废」动作）" });

        var 日期 = body.检验日期?.Date ?? DateTime.Today;
        if (body.下次检验日期 is DateTime 下次 && 下次.Date <= 日期)
            return BadRequest(new { message = "下次检验日期必须晚于本次检验日期" });

        try
        {
            await _data.查询SqlAsync("""
                CALL public."usp_钢瓶_检验"(@id,@d,@t,@org,@result,@next,@report,@fee,@emp,@note)
                """, ct,
                ("@id", id), ("@d", 日期), ("@t", 类型), ("@org", body.检验机构.Trim()),
                ("@result", 结论), ("@next", body.下次检验日期), ("@report", body.检验报告号),
                ("@fee", body.检验费用), ("@emp", _me.员工编号), ("@note", body.备注));
            var r = await _data.查询SqlAsync("""
                SELECT (SELECT MAX("检验记录编号") FROM "钢瓶检验记录" WHERE "钢瓶编号" = @id) AS "检验记录编号",
                       (SELECT "状态" FROM "钢瓶档案" WHERE "钢瓶编号" = @id) AS "状态",
                       (SELECT "上次检验日期" FROM "钢瓶档案" WHERE "钢瓶编号" = @id) AS "上次检验日期",
                       (SELECT "下次检验日期" FROM "钢瓶档案" WHERE "钢瓶编号" = @id) AS "下次检验日期";
                """, ct, ("@id", id));
            return Ok(new
            {
                ok = true,
                钢瓶编号 = id,
                检验记录编号 = r[0]["检验记录编号"],
                状态 = r[0]["状态"],
                上次检验日期 = r[0]["上次检验日期"],
                下次检验日期 = r[0]["下次检验日期"]
            });
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    public sealed class 报废入参
    {
        public DateTime? 报废日期 { get; set; }
        public string? 报废原因 { get; set; }
        public string? 报废原因说明 { get; set; }
        public decimal? 残值金额 { get; set; }
        public string? 备注 { get; set; }
    }

    /// <summary>报废：建一张单只钢瓶的报废单 → usp_钢瓶_报废（状态→报废 + 轨迹 + 产权变更）。</summary>
    [HttpPost("{id:int}/scrap")]
    [资源("Cyl.Scrap")]
    [需要权限("ADD")]
    public async Task<IActionResult> 报废(int id, [FromBody] 报废入参 body, CancellationToken ct)
    {
        var 档 = await _data.查询SqlAsync(
            "SELECT 状态 FROM dbo.钢瓶档案 WHERE 钢瓶编号 = @id", ct, ("@id", id));
        if (档.Count == 0) return NotFound(new { message = "钢瓶不存在" });
        var 状态 = Convert.ToString(档[0]["状态"]);
        if (状态 is "报废" or "遗失")
            return Conflict(new { message = $"钢瓶当前状态为「{状态}」，不能重复报废" });

        var 原因 = body.报废原因?.Trim() ?? "";
        if (!允许报废原因.Contains(原因))
            return BadRequest(new { message = "报废原因只能选 检验报废 / 腐蚀损坏 / 事故损坏 / 超期 / 其他" });
        if (body.残值金额 is < 0)
            return BadRequest(new { message = "残值金额不能为负" });

        var 日期 = body.报废日期?.Date ?? DateTime.Today;
        try
        {
            var noRow = await _data.查询SqlAsync("CALL public.\"usp_取单据编号\"('cylinder_scrap',@no,@d)", ct, ("@no", null), ("@d", 日期));
            var _nr = noRow.FirstOrDefault();
            var no = _nr is not null && _nr.TryGetValue("p_单据编号", out var _nv) ? Convert.ToString(_nv) ?? "" : "";
            var docRow = await _data.查询SqlAsync("""
                INSERT INTO "钢瓶报废单" ("单据号码","报废日期","报废原因","报废原因说明","残值金额","是否已登记销毁","单据状态","经办人编号","备注","创建人")
                VALUES (@no,@d,@reason,@note,@residual,false,2,@emp,@remark,@emp) RETURNING "报废单编号";
                """, ct,
                ("@no", no), ("@d", 日期), ("@reason", 原因), ("@note", body.报废原因说明),
                ("@residual", body.残值金额 ?? 0m), ("@emp", _me.员工编号), ("@remark", body.备注));
            var doc = docRow.FirstOrDefault() is { } _dr && _dr.TryGetValue("报废单编号", out var _dv) && _dv is not null ? Convert.ToInt32(_dv) : 0;
            await _data.查询SqlAsync("""
                INSERT INTO "钢瓶报废单明细" ("报废单编号","钢瓶编号","残值金额","备注") VALUES (@doc,@id,@residual,@note);
                """, ct, ("@doc", doc), ("@id", id), ("@residual", body.残值金额 ?? 0m), ("@note", body.报废原因说明));
            await _data.查询SqlAsync("CALL public.\"usp_钢瓶_报废\"(@doc)", ct, ("@doc", doc));
            var r = await _data.查询SqlAsync("""
                SELECT @doc AS "报废单编号", @no AS "单据号码",
                       (SELECT "状态" FROM "钢瓶档案" WHERE "钢瓶编号" = @id) AS "状态",
                       (SELECT "报废日期" FROM "钢瓶档案" WHERE "钢瓶编号" = @id) AS "报废日期";
                """, ct, ("@doc", doc), ("@no", no), ("@id", id));
            return Ok(new
            {
                ok = true,
                钢瓶编号 = id,
                报废单编号 = r[0]["报废单编号"],
                单据号码 = r[0]["单据号码"],
                状态 = r[0]["状态"],
                报废日期 = r[0]["报废日期"]
            });
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    public sealed class 遗失入参
    {
        public int? 客户编号 { get; set; }
        public DateTime? 遗失日期 { get; set; }
        public string? 赔偿方式 { get; set; }
        public decimal? 赔偿金额 { get; set; }
        public string? 备注 { get; set; }
    }

    /// <summary>遗失：建一张单只钢瓶的遗失单 → usp_钢瓶_遗失（状态→遗失 + 轨迹）。</summary>
    [HttpPost("{id:int}/lost")]
    [资源("Cyl.Scrap")]
    [需要权限("ADD")]
    public async Task<IActionResult> 遗失(int id, [FromBody] 遗失入参 body, CancellationToken ct)
    {
        var 档 = await _data.查询SqlAsync(
            "SELECT 状态, 当前客户编号 FROM dbo.钢瓶档案 WHERE 钢瓶编号 = @id", ct, ("@id", id));
        if (档.Count == 0) return NotFound(new { message = "钢瓶不存在" });
        var 状态 = Convert.ToString(档[0]["状态"]);
        if (状态 is "报废" or "遗失")
            return Conflict(new { message = $"钢瓶当前状态为「{状态}」，不能重复登记遗失" });

        if (body.客户编号 is null or <= 0)
            return BadRequest(new { message = "请选择报失客户" });
        var 客户 = await _data.查询SqlAsync(
            "SELECT 客户编号 FROM dbo.客户 WHERE 客户编号 = @c", ct, ("@c", body.客户编号));
        if (客户.Count == 0) return BadRequest(new { message = "客户不存在" });

        var 方式 = string.IsNullOrWhiteSpace(body.赔偿方式) ? "现金" : body.赔偿方式.Trim();
        if (!允许赔偿方式.Contains(方式))
            return BadRequest(new { message = "赔偿方式只能选 现金 / 押金抵扣 / 免赔 / 换瓶" });
        if (body.赔偿金额 is < 0)
            return BadRequest(new { message = "赔偿金额不能为负" });

        var 日期 = body.遗失日期?.Date ?? DateTime.Today;
        try
        {
            var noRow = await _data.查询SqlAsync("CALL public.\"usp_取单据编号\"('cylinder_lost',@no,@d)", ct, ("@no", null), ("@d", 日期));
            var _nr = noRow.FirstOrDefault();
            var no = _nr is not null && _nr.TryGetValue("p_单据编号", out var _nv) ? Convert.ToString(_nv) ?? "" : "";
            var docRow = await _data.查询SqlAsync("""
                INSERT INTO "钢瓶遗失单" ("单据号码","钢瓶编号","客户编号","遗失日期","赔偿方式","赔偿金额","是否已收赔","单据状态","经办人编号","备注","创建人")
                VALUES (@no,@id,@cust,@d,@way,@amount,false,0,@emp,@remark,@emp) RETURNING "遗失单编号";
                """, ct,
                ("@no", no), ("@id", id), ("@cust", body.客户编号), ("@d", 日期), ("@way", 方式),
                ("@amount", body.赔偿金额 ?? 0m), ("@emp", _me.员工编号), ("@remark", body.备注));
            var doc = docRow.FirstOrDefault() is { } _dr && _dr.TryGetValue("遗失单编号", out var _dv) && _dv is not null ? Convert.ToInt32(_dv) : 0;
            await _data.查询SqlAsync("CALL public.\"usp_钢瓶_遗失\"(@doc)", ct, ("@doc", doc));
            var r = await _data.查询SqlAsync("""
                SELECT @doc AS "遗失单编号", @no AS "单据号码",
                       (SELECT "状态" FROM "钢瓶档案" WHERE "钢瓶编号" = @id) AS "状态";
                """, ct, ("@doc", doc), ("@no", no), ("@id", id));
            return Ok(new
            {
                ok = true,
                钢瓶编号 = id,
                遗失单编号 = r[0]["遗失单编号"],
                单据号码 = r[0]["单据号码"],
                状态 = r[0]["状态"]
            });
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    public sealed class 移动入参
    {
        public int 目标仓库编号 { get; set; }
        public DateTime? 业务日期 { get; set; }
        public string? 备注 { get; set; }
    }

    /// <summary>
    /// 仓间移动：只改一物一码的当前位置/轨迹，不动库存数量结存。
    /// 只允许「在库」且已有当前仓库的瓶，目标必须是有效的非车仓，且与当前仓不同。
    /// </summary>
    [HttpPost("{id:int}/move")]
    [需要权限("EDIT")]
    public async Task<IActionResult> 移动(int id, [FromBody] 移动入参 body, CancellationToken ct)
    {
        if (body.目标仓库编号 <= 0)
            return BadRequest(new { message = "请选择目标仓库" });

        var 档 = await _data.查询SqlAsync(
            "SELECT 状态, 当前仓库编号 FROM dbo.钢瓶档案 WHERE 钢瓶编号 = @id", ct, ("@id", id));
        if (档.Count == 0) return NotFound(new { message = "钢瓶不存在" });
        var 状态 = Convert.ToString(档[0]["状态"]);
        var 当前仓 = 档[0]["当前仓库编号"] is null ? 0 : Convert.ToInt32(档[0]["当前仓库编号"]);
        if (状态 != "在库" || 当前仓 <= 0)
            return Conflict(new { message = $"只有「在库」且已登记当前仓库的钢瓶才能仓间移动（当前：{状态}）" });
        if (当前仓 == body.目标仓库编号)
            return BadRequest(new { message = "目标仓库与当前仓库相同，无需移动" });

        var 仓 = await _data.查询SqlAsync(
            "SELECT 是否有效, 是否车仓 FROM dbo.仓库 WHERE 仓库编号 = @w", ct, ("@w", body.目标仓库编号));
        if (仓.Count == 0) return BadRequest(new { message = "目标仓库不存在" });
        if (Convert.ToBoolean(仓[0]["是否车仓"]))
            return BadRequest(new { message = "车仓请通过装车/配送进入，不能作为仓间移动的目标" });
        if (!Convert.ToBoolean(仓[0]["是否有效"]))
            return BadRequest(new { message = "目标仓库已停用" });

        var 日期 = body.业务日期?.Date ?? DateTime.Today;
        try
        {
            await _data.查询SqlAsync("""
                CALL public."usp_钢瓶_移动"(@id,'仓间移动','在库',@w,NULL,NULL,NULL,NULL,NULL,@d,@emp,@note)
                """, ct,
                ("@id", id), ("@w", body.目标仓库编号), ("@d", 日期),
                ("@emp", _me.员工编号), ("@note", body.备注));
            var r = await _data.查询SqlAsync("""
                SELECT "状态", "当前仓库编号", "当前客户编号", "最后流转时间"
                FROM "钢瓶档案" WHERE "钢瓶编号" = @id;
                """, ct, ("@id", id));
            return Ok(new { ok = true, 钢瓶编号 = id, 状态 = r[0]["状态"],
                            当前仓库编号 = r[0]["当前仓库编号"], 最后流转时间 = r[0]["最后流转时间"] });
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
