using GasInventory.Api.Auth;
using GasInventory.Api.Services;
using GasInventory.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace GasInventory.Api.Controllers;

/// <summary>
/// 配送调度（kind = dispatch）：计划 → 转任务 → 装车 → 出车 → 回场。
/// 五个动作全部调用库里的 usp_配送_*，不直接写单据/库存表。
///
/// 列表按「计划」为一行：计划未转任务时也必须在列表里可见，
/// 否则前端没有入口执行 convert。
/// </summary>
[ApiController]
[Route("api/orders/dispatch")]
[Authorize]
[资源("Dist.Task")]
public class DispatchController : ControllerBase
{
    private readonly 数据服务 _data;
    private readonly 当前登录 _me;

    public DispatchController(数据服务 data, 当前登录 me)
    {
        _data = data;
        _me = me;
    }

    // ================================================================ 查询

    /// <summary>
    /// 配送任务列表。阶段：0 计划 / 1 已转任务 / 2 已装车 / 3 在途 / 4 已回场。
    /// </summary>
    [HttpGet]
    [需要权限("VIEW")]
    public async Task<IActionResult> 列表(
        int page = 1, int size = 20, string? keyword = null,
        [FromQuery(Name = "阶段")] int? 阶段 = null,
        DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var where = new List<string>();
        var ps = new List<(string 名, object? 值)>();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            where.Add("""
                (计划单据号码 LIKE @kw OR ISNULL(任务单据号码, '') LIKE @kw
                 OR ISNULL(车牌, '') LIKE @kw OR ISNULL(司机姓名, '') LIKE @kw
                 OR ISNULL(路线名称, '') LIKE @kw)
                """);
            ps.Add(("@kw", $"%{keyword.Trim()}%"));
        }
        if (阶段 is not null)
        {
            where.Add("阶段 = @stage");
            ps.Add(("@stage", 阶段.Value));
        }
        if (from is not null)
        {
            where.Add("计划日期 >= @from");
            ps.Add(("@from", from.Value.Date));
        }
        if (to is not null)
        {
            where.Add("计划日期 < @to");
            ps.Add(("@to", to.Value.Date.AddDays(1)));
        }

        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);

        var cte = """
            ;WITH x AS (
                SELECT p.计划编号, p.单据号码 AS 计划单据号码, p.计划日期, p.所属网点编号,
                       n.网点名称, p.车辆编号, v.车牌, p.司机编号, e.姓名 AS 司机姓名,
                       p.路线编号, r.路线名称, p.计划出车时间, p.预估客户数, p.预估瓶数,
                       p.装车瓶数, p.单据状态 AS 计划状态, p.备注 AS 计划备注, p.配送任务编号,
                       t.任务编号, t.单据号码 AS 任务单据号码, t.单据状态 AS 任务状态,
                       t.计划客户数, t.计划瓶数, t.实际客户数, t.实际瓶数, t.出车时间, t.回场时间,
                       (SELECT TOP (1) l.装车单编号 FROM dbo.装车单 l
                         WHERE l.任务编号 = t.任务编号 ORDER BY l.装车单编号 DESC) AS 装车单编号,
                       (SELECT TOP (1) u.回场单编号 FROM dbo.回场单 u
                         WHERE u.任务编号 = t.任务编号 ORDER BY u.回场单编号 DESC) AS 回场单编号,
                       CASE WHEN t.任务编号 IS NULL THEN 0
                            WHEN t.单据状态 = 0 THEN 1
                            WHEN t.单据状态 = 1 THEN 2
                            WHEN t.单据状态 = 2 THEN 3
                            WHEN t.单据状态 = 3 THEN 4
                            ELSE 9 END AS 阶段
                  FROM dbo.配送计划 p
                  LEFT JOIN dbo.配送任务 t ON t.任务编号 = p.配送任务编号
                  LEFT JOIN dbo.经营网点 n ON n.网点编号 = p.所属网点编号
                  LEFT JOIN dbo.车辆 v ON v.车辆编号 = p.车辆编号
                  LEFT JOIN dbo.员工 e ON e.员工编号 = p.司机编号
                  LEFT JOIN dbo.配送路线 r ON r.路线编号 = p.路线编号
            )
            """;

        var total = await _data.标量Async<int>($"{cte} SELECT COUNT(1) FROM x {whereSql};", ct, ps.ToArray());
        var rows = await _data.查询SqlAsync($"""
            {cte}
            SELECT * FROM x {whereSql}
             ORDER BY 计划日期 DESC, 计划编号 DESC
             OFFSET {(page - 1) * size} ROWS FETCH NEXT {size} ROWS ONLY;
            """, ct, ps.ToArray());
        return Ok(new { total, items = rows });
    }

    /// <summary>新建计划弹窗需要的下拉：配送路线（车辆/仓库/员工走 /api/lookups）</summary>
    [HttpGet("options")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 选项(CancellationToken ct)
    {
        var 路线 = await _data.查询SqlAsync("""
            SELECT 路线编号, 路线编码, 路线名称, 所属网点编号
              FROM dbo.配送路线
             WHERE 是否有效 = 1
             ORDER BY 排序号, 路线编号;
            """, ct);
        return Ok(new { 路线 });
    }

    /// <summary>
    /// 只读安全门：探针据此确认 API 连的是库副本（GasInventory_verify），
    /// 而不是真库 —— 装车/回场会动库存流水，流水只增不改，清不掉。
    /// </summary>
    [HttpGet("health")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 健康检查(CancellationToken ct)
    {
        var r = await _data.查询SqlAsync("""
            SELECT DB_NAME() AS 库, @@SERVERNAME AS 服务器,
                   (SELECT COUNT(1) FROM dbo.配送任务) AS 任务数;
            """, ct);
        return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
    }

    [HttpGet("plans/{id:int}")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 计划详情(int id, CancellationToken ct)
    {
        var 计划 = await 读计划(id, ct);
        if (计划 is null) return NotFound(new { message = "配送计划不存在" });

        var 任务编号 = 取编号(计划, "配送任务编号");
        if (任务编号 is null)
        {
            return Ok(new
            {
                计划,
                任务 = (Dictionary<string, object?>?)null,
                配送明细 = new List<Dictionary<string, object?>>(),
                装车单 = (Dictionary<string, object?>?)null,
                装车明细 = new List<Dictionary<string, object?>>(),
                回场单 = (Dictionary<string, object?>?)null,
                回场明细 = new List<Dictionary<string, object?>>(),
                回场实盘 = new List<Dictionary<string, object?>>()
            });
        }

        var 任务 = await 读任务(任务编号.Value, ct);
        var 子 = await 读任务子表(任务编号.Value, ct);
        return Ok(new { 计划, 任务, 子.配送明细, 子.装车单, 子.装车明细, 子.回场单, 子.回场明细, 子.回场实盘 });
    }

    [HttpGet("tasks/{id:int}")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 任务详情(int id, CancellationToken ct)
    {
        var 任务 = await 读任务(id, ct);
        if (任务 is null) return NotFound(new { message = "配送任务不存在" });

        var 计划编号 = await _data.标量Async<int?>(
            "SELECT TOP (1) 计划编号 FROM dbo.配送计划 WHERE 配送任务编号 = @id ORDER BY 计划编号 DESC",
            ct, ("@id", id));
        var 计划 = 计划编号 is null ? null : await 读计划(计划编号.Value, ct);
        var 子 = await 读任务子表(id, ct);
        return Ok(new { 计划, 任务, 子.配送明细, 子.装车单, 子.装车明细, 子.回场单, 子.回场明细, 子.回场实盘 });
    }

    // ================================================================ 动作

    public sealed class 计划入参
    {
        public int 网点编号 { get; set; }
        public int 车辆编号 { get; set; }
        public int 出车仓库编号 { get; set; }
        public DateTime? 计划日期 { get; set; }
        public int? 路线编号 { get; set; }
        public int? 司机编号 { get; set; }
        public int? 跟车编号 { get; set; }
        // 前端 el-date-picker 的 value-format 是 "YYYY-MM-DD HH:mm:ss"（空格分隔），
        // System.Text.Json 默认只认 ISO 的 T 分隔；这里收字符串再 TryParse，两种都吃。
        public string? 计划出车时间 { get; set; }
        public string? 备注 { get; set; }
    }

    [HttpPost("plan")]
    [需要权限("ADD")]
    public async Task<IActionResult> 生成计划([FromBody] 计划入参 body, CancellationToken ct)
    {
        if (body.网点编号 <= 0 || body.车辆编号 <= 0 || body.出车仓库编号 <= 0)
            return BadRequest(new { message = "请选择网点、车辆和出车仓库" });

        var 出车时间 = DateTime.TryParse(body.计划出车时间, out var t) ? t : (DateTime?)null;

        try
        {
            var call = await _data.查询SqlAsync("""
                CALL public."usp_配送_生成计划"(@net,@veh,@wh,@d,@route,@driver,@mate,@out,@emp,@note,@p)
                """, ct,
                ("@net", body.网点编号), ("@veh", body.车辆编号), ("@wh", body.出车仓库编号),
                ("@d", body.计划日期), ("@route", body.路线编号), ("@driver", body.司机编号),
                ("@mate", body.跟车编号), ("@out", 出车时间), ("@emp", _me.员工编号),
                ("@note", body.备注), ("@p", null));
            var _r0 = call.FirstOrDefault();
            var pid = _r0 is not null && _r0.TryGetValue("p_计划编号", out var _v0) && _v0 is not null ? Convert.ToInt32(_v0) : 0;
            var r = await _data.查询SqlAsync("""
                SELECT @p AS 计划编号, 单据号码, 预估客户数, 预估瓶数, 车仓仓库编号
                  FROM "配送计划" WHERE 计划编号 = @p;
                """, ct, ("@p", pid));
            return Ok(r.FirstOrDefault());
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("plans/{id:int}/convert")]
    [需要权限("EDIT")]
    public async Task<IActionResult> 转任务(int id, CancellationToken ct)
    {
        try
        {
            var call = await _data.查询SqlAsync("""
                CALL public."usp_配送_计划转任务"(@p,@e,@t)
                """, ct, ("@p", id), ("@e", _me.员工编号), ("@t", null));
            var _r0 = call.FirstOrDefault();
            var tid = _r0 is not null && _r0.TryGetValue("p_任务编号", out var _v0) && _v0 is not null ? Convert.ToInt32(_v0) : 0;
            var r = await _data.查询SqlAsync("""
                SELECT @t AS 任务编号, 单据号码, 计划客户数, 计划瓶数, 单据状态
                  FROM "配送任务" WHERE 任务编号 = @t;
                """, ct, ("@t", tid));
            return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("tasks/{id:int}/load")]
    [需要权限("EDIT")]
    public async Task<IActionResult> 装车(int id, CancellationToken ct)
    {
        try
        {
            var call = await _data.查询SqlAsync("""
                CALL public."usp_配送_任务装车"(@t,@e,NULL,@l)
                """, ct, ("@t", id), ("@e", _me.员工编号), ("@l", null));
            var _r0 = call.FirstOrDefault();
            var lid = _r0 is not null && _r0.TryGetValue("p_装车单编号", out var _v0) && _v0 is not null ? Convert.ToInt32(_v0) : 0;
            var r = await _data.查询SqlAsync("""
                SELECT @l AS 装车单编号, 单据号码, 装车瓶数, 调拨单编号,
                       (SELECT 单据状态 FROM "配送任务" WHERE 任务编号 = @t) AS 任务状态
                  FROM "装车单" WHERE 装车单编号 = @l;
                """, ct, ("@t", id), ("@l", lid));
            return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("tasks/{id:int}/out")]
    [需要权限("EDIT")]
    public async Task<IActionResult> 出车(int id, CancellationToken ct)
    {
        try
        {
            await _data.查询SqlAsync("""
                CALL public."usp_配送_任务出车"(@t,NULL,@e)
                """, ct, ("@t", id), ("@e", _me.员工编号));
            var r = await _data.查询SqlAsync("""
                SELECT 单据状态 AS 任务状态, 出车时间 FROM "配送任务" WHERE 任务编号 = @t;
                """, ct, ("@t", id));
            return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("tasks/{id:int}/return")]
    [需要权限("EDIT")]
    public async Task<IActionResult> 回场(int id, CancellationToken ct)
    {
        try
        {
            var call = await _data.查询SqlAsync("""
                CALL public."usp_配送_任务回场"(@t,NULL,@e,NULL,NULL,@u)
                """, ct, ("@t", id), ("@e", _me.员工编号), ("@u", null));
            var _r0 = call.FirstOrDefault();
            var uid = _r0 is not null && _r0.TryGetValue("p_回场单编号", out var _v0) && _v0 is not null ? Convert.ToInt32(_v0) : 0;
            var r = await _data.查询SqlAsync("""
                SELECT @u AS 回场单编号, 单据号码, 车仓余瓶数, 回收空瓶数,
                       差异瓶数, 盘点单编号,
                       (SELECT 单据状态 FROM "配送任务" WHERE 任务编号 = @t) AS 任务状态
                  FROM "回场单" WHERE 回场单编号 = @u;
                """, ct, ("@t", id), ("@u", uid));
            return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
        }
        catch (NpgsqlException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // ================================================================ 内部查询

    private async Task<Dictionary<string, object?>?> 读计划(int id, CancellationToken ct)
    {
        var rows = await _data.查询SqlAsync("""
            SELECT p.*, n.网点名称, v.车牌, e.姓名 AS 司机姓名, m.姓名 AS 跟车姓名,
                   r.路线名称
              FROM dbo.配送计划 p
              LEFT JOIN dbo.经营网点 n ON n.网点编号 = p.所属网点编号
              LEFT JOIN dbo.车辆 v ON v.车辆编号 = p.车辆编号
              LEFT JOIN dbo.员工 e ON e.员工编号 = p.司机编号
              LEFT JOIN dbo.员工 m ON m.员工编号 = p.跟车编号
              LEFT JOIN dbo.配送路线 r ON r.路线编号 = p.路线编号
             WHERE p.计划编号 = @id;
            """, ct, ("@id", id));
        return rows.FirstOrDefault();
    }

    private async Task<Dictionary<string, object?>?> 读任务(int id, CancellationToken ct)
    {
        var rows = await _data.查询SqlAsync("""
            SELECT t.*, n.网点名称, v.车牌, e.姓名 AS 司机姓名, m.姓名 AS 跟车姓名
              FROM dbo.配送任务 t
              LEFT JOIN dbo.经营网点 n ON n.网点编号 = t.所属网点编号
              LEFT JOIN dbo.车辆 v ON v.车辆编号 = t.车辆编号
              LEFT JOIN dbo.员工 e ON e.员工编号 = t.司机编号
              LEFT JOIN dbo.员工 m ON m.员工编号 = t.跟车编号
             WHERE t.任务编号 = @id;
            """, ct, ("@id", id));
        return rows.FirstOrDefault();
    }

    private sealed record 任务子表(
        List<Dictionary<string, object?>> 配送明细,
        Dictionary<string, object?>? 装车单,
        List<Dictionary<string, object?>> 装车明细,
        Dictionary<string, object?>? 回场单,
        List<Dictionary<string, object?>> 回场明细,
        List<Dictionary<string, object?>> 回场实盘);

    private async Task<任务子表> 读任务子表(int taskId, CancellationToken ct)
    {
        var 配送明细 = await _data.查询SqlAsync("""
            SELECT pd.配送明细编号, pd.行号, pd.客户编号, k.客户编码, k.客户简称 AS 客户名称,
                   k.联系电话, pd.订单编号, o.单据号码 AS 订单单据号码, pd.收货地址,
                   pd.送达顺序, pd.计划瓶数, pd.送达瓶数, pd.回收空瓶数, pd.送达时间,
                   pd.签收人, pd.签收方式, pd.送气单编号, pd.配送状态, pd.异常说明
              FROM dbo.配送明细 pd
              LEFT JOIN dbo.客户 k ON k.客户编号 = pd.客户编号
              LEFT JOIN dbo.销售订单 o ON o.订单编号 = pd.订单编号
             WHERE pd.任务编号 = @t
             ORDER BY pd.行号;
            """, ct, ("@t", taskId));

        var 装车单 = (await _data.查询SqlAsync("""
            SELECT TOP (1) l.*, w1.仓库名称 AS 调出仓库名称, w2.仓库名称 AS 车仓名称,
                   e.姓名 AS 装车人姓名, db.单据号码 AS 调拨单号码
              FROM dbo.装车单 l
              LEFT JOIN dbo.仓库 w1 ON w1.仓库编号 = l.调出仓库编号
              LEFT JOIN dbo.仓库 w2 ON w2.仓库编号 = l.车仓仓库编号
              LEFT JOIN dbo.员工 e ON e.员工编号 = l.装车人编号
              LEFT JOIN dbo.库存调拨单 db ON db.调拨单编号 = l.调拨单编号
             WHERE l.任务编号 = @t
             ORDER BY l.装车单编号 DESC;
            """, ct, ("@t", taskId))).FirstOrDefault();

        var 装车明细 = 装车单 is null
            ? new List<Dictionary<string, object?>>()
            : await _data.查询SqlAsync("""
                SELECT d.行号, d.商品编号, p.商品编码, p.商品名称, d.批次编号,
                       d.装车数量, d.押瓶数量, d.成本单价
                  FROM dbo.装车单明细 d
                  LEFT JOIN dbo.商品 p ON p.商品编号 = d.商品编号
                 WHERE d.装车单编号 = @id
                 ORDER BY d.行号;
                """, ct, ("@id", 取编号(装车单, "装车单编号")));

        var 回场单 = (await _data.查询SqlAsync("""
            SELECT TOP (1) u.*, w1.仓库名称 AS 车仓名称, w2.仓库名称 AS 调入仓库名称,
                   e.姓名 AS 回场人姓名, st.单据号码 AS 盘点单号码,
                   db.单据号码 AS 调拨单号码
              FROM dbo.回场单 u
              LEFT JOIN dbo.仓库 w1 ON w1.仓库编号 = u.车仓仓库编号
              LEFT JOIN dbo.仓库 w2 ON w2.仓库编号 = u.调入仓库编号
              LEFT JOIN dbo.员工 e ON e.员工编号 = u.回场人编号
              LEFT JOIN dbo.库存盘点单 st ON st.盘点单编号 = u.盘点单编号
              LEFT JOIN dbo.库存调拨单 db ON db.调拨单编号 = u.调拨单编号
             WHERE u.任务编号 = @t
             ORDER BY u.回场单编号 DESC;
            """, ct, ("@t", taskId))).FirstOrDefault();

        var 回场明细 = 回场单 is null
            ? new List<Dictionary<string, object?>>()
            : await _data.查询SqlAsync("""
                SELECT d.行号, d.商品编号, p.商品名称, d.批次编号, d.库存状态代码,
                       d.权属代码, d.装车数量, d.送达数量, d.回收数量, d.留车数量, d.成本单价
                  FROM dbo.回场单明细 d
                  LEFT JOIN dbo.商品 p ON p.商品编号 = d.商品编号
                 WHERE d.回场单编号 = @id
                 ORDER BY d.行号;
                """, ct, ("@id", 取编号(回场单, "回场单编号")));

        var 回场实盘 = await _data.查询SqlAsync("""
            SELECT r.*, p.商品名称
              FROM dbo.回场实盘 r
              LEFT JOIN dbo.商品 p ON p.商品编号 = r.商品编号
             WHERE r.任务编号 = @t
             ORDER BY r.商品编号, r.批次编号;
            """, ct, ("@t", taskId));

        return new 任务子表(配送明细, 装车单, 装车明细, 回场单, 回场明细, 回场实盘);
    }

    private static int? 取编号(IReadOnlyDictionary<string, object?>? row, string 列)
    {
        if (row is null || !row.TryGetValue(列, out var v) || v is null || v is DBNull) return null;
        return Convert.ToInt32(v);
    }
}
