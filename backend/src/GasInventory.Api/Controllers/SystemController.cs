using System.Diagnostics;
using GasInventory.Api.Auth;
using GasInventory.Api.Services;
using GasInventory.Domain.Entities;
using GasInventory.Infrastructure.Data;
using GasInventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GasInventory.Api.Controllers;

/// <summary>
/// 系统管理：权限矩阵、登录日志、操作日志、系统参数、健康检查。
///
/// v3 口径（批次 6 第 6 步对齐）：登录记录用 工号或账号/客户端IP（v2 是 工号/登录IP）；
/// 系统参数是 参数名+值类型+参数分组+参数说明（v2 是 参数类型/参数分组）；
/// 操作日志读 vw_操作日志（模块/功能/请求方法/请求地址/响应码/耗时/成败）。
///
/// ⚠ 挂 <see cref="资源Attribute"/> 之前，这一组接口只校验登录态 —— 实测过
/// "送瓶员能读操作日志、出纳（只有 view）能改系统参数"，见 权限过滤器 的说明。
/// 加了类级资源之后，每个动作再按 [需要权限] 细分：读 view / 写 edit。
/// </summary>
[ApiController]
[Route("api/system")]
[Authorize]
[资源("Sys.User")]
public class SystemController : ControllerBase
{
    private readonly GasInventoryDbContext _db;
    private readonly 数据服务 _data;
    private readonly 当前登录 _me;

    public SystemController(GasInventoryDbContext db, 数据服务 data, 当前登录 me)
    {
        _db = db;
        _data = data;
        _me = me;
    }

    // ================================================================ 权限矩阵

    /// <summary>权限矩阵：岗位 × 资源 × 权限项 的全部授权点</summary>
    [HttpGet("permissions/matrix")]
    [资源("Sys.User")]
    public async Task<IActionResult> 矩阵()
    {
        var 岗位 = await _db.岗位.AsNoTracking().OrderBy(x => x.排序号).ThenBy(x => x.岗位编号)
            .Select(x => new { x.岗位编号, x.岗位代码, x.岗位名称, x.是否超级管理员, x.状态 }).ToListAsync();
        var 资源 = await _db.权限资源.AsNoTracking().OrderBy(x => x.排序号)
            .Select(x => new { x.资源编号, x.资源名称, x.资源代码, x.模块, x.对象名, x.排序号 }).ToListAsync();
        var 权限 = await _db.权限项.AsNoTracking().OrderBy(x => x.排序号)
            .Select(x => new { x.权限编号, x.权限代码, x.权限名称 }).ToListAsync();
        var 授权 = await _db.岗位权限.AsNoTracking()
            .Select(x => new { x.岗位编号, x.资源编号, x.权限编号 }).ToListAsync();

        return Ok(new { 岗位, 资源, 权限, 授权 });
    }

    /// <summary>保存某个岗位的授权（整岗覆盖式保存）</summary>
    [HttpPost("permissions/{岗位编号:int}")]
    [资源("Sys.User")]
    [需要权限("EDIT")]
    public async Task<IActionResult> 保存矩阵(int 岗位编号, [FromBody] List<岗位权限> 项)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        await _db.岗位权限.Where(x => x.岗位编号 == 岗位编号).ExecuteDeleteAsync();
        foreach (var p in 项.DistinctBy(x => (x.资源编号, x.权限编号)))
        {
            await _db.岗位权限.AddAsync(new 岗位权限
            {
                岗位编号 = 岗位编号,
                资源编号 = p.资源编号,
                权限编号 = p.权限编号
            });
        }
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return Ok(new { ok = true, count = 项.Count });
    }

    // ================================================================ 登录日志

    /// <summary>
    /// 登录日志（v3）：v2 的 登录IP 在 v3 叫 客户端IP，工号在 v3 落在 工号或账号。
    /// 前端契约仍要 工号/姓名/登录IP，这里在 SQL 里别名回来，页面不用改字段名。
    /// </summary>
    [HttpGet("login-logs")]
    [资源("Sys.Log")]
    public async Task<IActionResult> 登录日志(int page = 1, int size = 30, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 500);

        var total = await _data.标量Async<long>("SELECT COUNT(1) FROM dbo.登录记录", ct);
        var rows = await _data.查询SqlAsync($"""
            SELECT l.登录记录编号,
                   ISNULL(e.工号, l.工号或账号)   AS 工号,
                   ISNULL(e.姓名, '')             AS 姓名,
                   CONVERT(char(19), l.登录时间, 120) AS 登录时间,
                   ISNULL(l.客户端IP, '')          AS 登录IP,
                   l.登录结果,
                   ISNULL(l.失败原因分类, '')      AS 失败原因分类,
                   ISNULL(l.失败原因, '')          AS 失败原因,
                   ISNULL(l.客户端, '')            AS 客户端,
                   ISNULL(l.登录方式, '')          AS 登录方式
            FROM dbo.登录记录 l
            LEFT JOIN dbo.员工 e ON e.员工编号 = l.员工编号
            ORDER BY l.登录时间 DESC, l.登录记录编号 DESC
            OFFSET {(page - 1) * size} ROWS FETCH NEXT {size} ROWS ONLY
            """, ct);

        return Ok(new { total, items = rows });
    }

    // ================================================================ 操作日志

    /// <summary>
    /// 操作日志（v3）：读 vw_操作日志，按 操作时间 倒序。
    /// 只有「操作类型」是服务端筛选（与前端 Oplogs.vue 的口径注一致），其余条件由前端在当页内过滤。
    /// 表里只记「改过数据的动作」——由 <see cref="审计过滤器"/> 在真实调用时写入，不预生成。
    /// </summary>
    [HttpGet("oplogs")]
    [资源("Sys.Audit")]
    public async Task<IActionResult> 操作日志(
        int page = 1, int size = 50,
        [FromQuery(Name = "操作类型")] string? 操作类型 = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);

        var where = "";
        var ps = new List<(string 名, object? 值)>();
        if (!string.IsNullOrWhiteSpace(操作类型))
        {
            where = "WHERE 操作类型 = @t";
            ps.Add(("@t", 操作类型.Trim()));
        }

        var total = await _data.标量Async<long>(
            $"SELECT COUNT(1) FROM dbo.vw_操作日志 {where}", ct, ps.ToArray());

        var rows = await _data.查询SqlAsync($"""
            SELECT 日志编号, CONVERT(char(19), 操作时间, 120) AS 操作时间,
                   员工编号, ISNULL(工号, '') AS 工号,
                   ISNULL(操作人姓名, ISNULL(操作人, '')) AS 操作人,
                   ISNULL(模块, '') AS 模块, ISNULL(功能, '') AS 功能,
                   操作类型, ISNULL(资源代码, '') AS 资源代码,
                   ISNULL(请求方法, '') AS 请求方法, ISNULL(请求地址, '') AS 请求地址,
                   响应码, 是否成功, ISNULL(错误信息, '') AS 错误信息, 耗时毫秒,
                   ISNULL(业务单据类型, '') AS 业务单据类型, 业务单据编号,
                   ISNULL(业务单号, '') AS 业务单号,
                   ISNULL(客户端, '') AS 客户端, ISNULL(客户端IP, '') AS 客户端IP
            FROM dbo.vw_操作日志
            {where}
            ORDER BY 操作时间 DESC, 日志编号 DESC
            OFFSET {(page - 1) * size} ROWS FETCH NEXT {size} ROWS ONLY
            """, ct, ps.ToArray());

        return Ok(new { total, items = rows });
    }

    // ================================================================ 系统参数

    /// <summary>
    /// 系统参数（v3）：值类型落在 值类型 列（v2 叫 参数类型），并带上 默认值/取值范围/是否可维护。
    /// 前端 系统参数行 的字段名保持不变（参数类型 由 SQL 别名回来）。
    /// ⚠ 只下发 是否可维护=1 的参数：不可维护的（策略级）改了也没人认，不该出现在可编辑列表里。
    /// </summary>
    [HttpGet("params")]
    [资源("Org.Param")]
    public async Task<IActionResult> 参数(CancellationToken ct) =>
        Ok(await _data.查询SqlAsync("""
            SELECT 参数名,
                   参数值,
                   值类型             AS 参数类型,
                   参数分组,
                   ISNULL(参数说明, '') AS 参数说明,
                   ISNULL(默认值, '')   AS 默认值,
                   ISNULL(取值范围, '') AS 取值范围,
                   是否可维护,
                   是否敏感,
                   生效范围,
                   CONVERT(char(19), 更新时间, 120) AS 更新时间
            FROM dbo.系统参数
            WHERE 是否可维护 = 1
            ORDER BY 参数分组, 参数名
            """, ct));

    /// <summary>
    /// 保存系统参数：前端提交的是**数组**（只含改动过的行），每行带 参数名/参数值/参数类型/参数分组/参数说明。
    /// v2 实现只收 Dictionary&lt;string,string?&gt;，前端一提交数组就是 400 —— 这里两种形状都收。
    /// 值按 值类型 校验（bool/int/decimal/date），校验不过整批拒绝并指明是哪几个参数，
    /// 不做"改一半"的静默部分成功。
    /// </summary>
    [HttpPost("params")]
    [资源("Org.Param")]
    [需要权限("EDIT")]
    public async Task<IActionResult> 保存参数([FromBody] System.Text.Json.JsonElement body, CancellationToken ct)
    {
        var 提交 = new List<(string 参数名, string? 参数值)>();
        if (body.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var el in body.EnumerateArray())
            {
                var 名 = el.TryGetProperty("参数名", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(名)) continue;
                string? 值 = null;
                if (el.TryGetProperty("参数值", out var v))
                    值 = v.ValueKind == System.Text.Json.JsonValueKind.Null ? null : v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : v.ToString();
                提交.Add((名.Trim(), 值));
            }
        }
        else if (body.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var p in body.EnumerateObject())
                提交.Add((p.Name, p.Value.ValueKind == System.Text.Json.JsonValueKind.Null ? null : p.Value.ToString()));
        }
        else return BadRequest(new { message = "参数必须是数组（前端契约）或对象（参数名→参数值）" });

        if (提交.Count == 0) return BadRequest(new { message = "没有可保存的参数" });

        var 名集 = 提交.Select(x => x.参数名).Distinct().ToList();
        var 库内 = await _data.查询SqlAsync("""
            SELECT 参数名, 值类型, 是否可维护, 取值范围
            FROM dbo.系统参数
            WHERE 参数名 IN (SELECT value FROM STRING_SPLIT(@names, ','))
            """, ct, ("@names", string.Join(",", 名集)));

        var 索引 = 库内.ToDictionary(r => Convert.ToString(r["参数名"])!, r => r);
        var 错误 = new List<string>();

        foreach (var (名, 值) in 提交)
        {
            if (!索引.TryGetValue(名, out var 行))
            {
                错误.Add($"{名}：参数不存在");
                continue;
            }
            if (Convert.ToBoolean(行["是否可维护"]) is false)
            {
                错误.Add($"{名}：该参数不可维护");
                continue;
            }
            var 类型 = Convert.ToString(行["值类型"]) ?? "string";
            if (!合法值(类型, 值, out var 规范))
                错误.Add($"{名}：值「{值}」不符合 {类型} 类型（取值范围 {行["取值范围"]}）");
        }

        if (错误.Count > 0)
            return BadRequest(new { message = "参数值不合法：" + string.Join("；", 错误) });

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        foreach (var (名, 值) in 提交)
        {
            var 类型 = Convert.ToString(索引[名]["值类型"]) ?? "string";
            合法值(类型, 值, out var 规范);
            await _data.查询SqlAsync("""
                UPDATE dbo.系统参数
                   SET 参数值 = @v, 更新时间 = SYSDATETIME(), 更新人 = @u
                 WHERE 参数名 = @n AND 是否可维护 = 1;
                """, ct, ("@v", 规范), ("@u", _me.员工编号), ("@n", 名));
        }
        await tx.CommitAsync(ct);
        return Ok(new { ok = true, count = 提交.Count });
    }

    /// <summary>按 值类型 校验并规范化（布尔统一 1/0，与库里其它 bool 参数的存法一致）</summary>
    private static bool 合法值(string 类型, string? 值, out string? 规范)
    {
        规范 = 值;
        if (string.IsNullOrWhiteSpace(值)) return true;   // 允许清空：由各自业务过程按默认值兜底

        switch (类型.ToLowerInvariant())
        {
            case "bool":
            case "boolean":
                switch (值.Trim().ToLowerInvariant())
                {
                    case "1": case "true": case "是": 规范 = "1"; return true;
                    case "0": case "false": case "否": 规范 = "0"; return true;
                    default: return false;
                }
            case "int":
                if (int.TryParse(值.Trim(), out var i)) { 规范 = i.ToString(); return true; }
                return false;
            case "decimal":
                if (decimal.TryParse(值.Trim(), out var d)) { 规范 = d.ToString("0.####"); return true; }
                return false;
            case "date":
            case "datetime":
                if (DateTime.TryParse(值.Trim(), out var t)) { 规范 = t.ToString("yyyy-MM-dd"); return true; }
                return false;
            default:
                return true;
        }
    }

    // ================================================================ 健康检查

    /// <summary>
    /// 健康检查：GET /api/system/health。
    /// 只回答"现在能不能干活"——库连得上吗、结构对不对、会计期间到哪、日志有没有在写。
    /// 任何一个探针失败都回 503（前端据此显示红色），但**仍返回完整明细**，不做"整体 500"。
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous]
    public async Task<IActionResult> 健康检查(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var 项 = new List<object>();
        var 全部通过 = true;

        async Task 探针(string 名, string 说明, Func<Task<string>> 跑)
        {
            try
            {
                var 值 = await 跑();
                项.Add(new { 名, 说明, 通过 = true, 值 });
            }
            catch (Exception ex)
            {
                全部通过 = false;
                项.Add(new { 名, 说明, 通过 = false, 值 = ex.Message });
            }
        }

        await 探针("数据库", "能否执行查询", async () =>
        {
            var v = await _data.标量Async<int>("SELECT 1", ct);
            return v == 1 ? "可连接" : "返回值异常";
        });

        await 探针("表结构", "v3 表数量（应为 186）", async () =>
            Convert.ToString(await _data.标量Async<int>("SELECT COUNT(1) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'", ct))!);

        await 探针("版本", "系统版本表最新一行", async () =>
        {
            var rows = await _data.查询SqlAsync(
                "SELECT TOP 1 版本名称, 域, CONVERT(char(19), 升级时间, 120) AS 升级时间 FROM dbo.系统版本 ORDER BY 版本号 DESC", ct);
            return rows.Count == 0 ? "无版本记录"
                : $"{rows[0]["版本名称"]}（{rows[0]["域"]}，{rows[0]["升级时间"]}）";
        });

        await 探针("会计期间", "最新期间与结账状态", async () =>
        {
            var rows = await _data.查询SqlAsync("""
                SELECT TOP 1 期间名称, 期间状态, 凭证张数 FROM dbo.会计期间
                ORDER BY 年度 DESC, 月份 DESC
                """, ct);
            if (rows.Count == 0) return "无会计期间";
            var r = rows[0];
            return $"{r["期间名称"]}：{(Convert.ToInt32(r["期间状态"]) == 1 ? "已结账" : "未结账")}，凭证 {r["凭证张数"]} 张";
        });

        await 探针("审计留痕", "操作日志/登录记录是否在写", async () =>
        {
            var r = (await _data.查询SqlAsync("""
                SELECT (SELECT COUNT(1) FROM dbo.操作日志) AS 操作日志,
                       (SELECT COUNT(1) FROM dbo.登录记录) AS 登录记录
                """, ct)).FirstOrDefault();
            return r is null ? "查询无结果" : $"操作日志 {r["操作日志"]} 条 / 登录记录 {r["登录记录"]} 条";
        });

        sw.Stop();
        var 结果 = new
        {
            状态 = 全部通过 ? "正常" : "异常",
            服务 = "GasInventory.Api",
            时间 = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            耗时毫秒 = sw.ElapsedMilliseconds,
            探针 = 项
        };
        return 全部通过 ? Ok(结果) : StatusCode(StatusCodes.Status503ServiceUnavailable, 结果);
    }
}
