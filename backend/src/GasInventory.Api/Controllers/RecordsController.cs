using System.Text.Json;
using GasInventory.Api.Auth;
using GasInventory.Api.Services;
using GasInventory.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GasInventory.Api.Controllers;

/// <summary>
/// 档案类通用 CRUD：客户、员工、钢瓶规格、供气商、打气车以及各类字典。
/// 表名/列名全部来自《元数据》白名单，前端只需按表名调用，新增一张档案表不用改后端。
/// 单据类（有明细、要算合计、要生成单号）不走这里，见 OrdersController。
/// </summary>
[ApiController]
[Route("api/records")]
[Authorize]
public class RecordsController : ControllerBase
{
    private readonly 数据服务 _data;

    public RecordsController(数据服务 data) => _data = data;

    /// <summary>
    /// 白名单：表不在通用维护范围内就 400。**权限不在这里判** ——
    /// 批次 20 把原来手写的 资源映射 + 检查() 收敛到 `权限过滤器`：
    /// 方法上的 `[资源按路由("table", "档案")]` + `[需要权限(代码)]` 就是唯一的接口级拦截，
    /// 映射与白名单都在 <see cref="动态资源表"/>（`权限资源`/`权限项` 已收紧到 Sys.User）。
    /// 白名单外的表在这里就 400，不会先吃一个 403（把"这张表不走通用接口"说成"你没权限"是假信号）。
    /// </summary>
    private IActionResult? 校验(string table) =>
        动态资源表.档案白名单.Contains(table)
            ? null
            // 批次 20：原来是 throw ArgumentException → 接口 500。参数写错要回 400，
            // 与 OrdersController 的「未知 kind → 400」同一口径（不能让前端把 500 当服务故障重试）。
            : BadRequest(new { message = $"表 {table} 不支持通用维护接口：单据类（有明细/状态机/过账过程）与复合主键表请走各自的业务接口" });

    [HttpGet("{table}")]
    [资源按路由("table", "档案")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 列表(string table, int page = 1, int size = 50,
        string? keyword = null, string? orderBy = null, bool desc = false, CancellationToken ct = default)
    {
        if (校验(table) is { } 拒绝) return 拒绝;
        var filters = new Dictionary<string, string?>();
        foreach (var q in Request.Query)
        {
            if (q.Key is "page" or "size" or "keyword" or "orderBy" or "desc") continue;
            filters[q.Key] = q.Value.ToString();
        }
        var r = await _data.查询Async(table, filters, keyword, page, size, orderBy, desc, null, ct);
        return Ok(new { total = r.总数, items = r.数据 });
    }

    [HttpGet("{table}/{id}")]
    [资源按路由("table", "档案")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 详情(string table, string id, CancellationToken ct)
    {
        if (校验(table) is { } 拒绝) return 拒绝;
        var m = 元数据.取(table);
        var row = await _data.按主键Async(table, 转换主键(m, id), ct);
        return row is null ? NotFound(new { message = "记录不存在" }) : Ok(row);
    }

    [HttpPost("{table}")]
    [资源按路由("table", "档案")]
    [需要权限("ADD")]
    public async Task<IActionResult> 新增(string table, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (校验(table) is { } 拒绝) return 拒绝;
        var values = ToDict(body);
        return Ok(new { id = await _data.新增Async(table, values, ct) });
    }

    [HttpPut("{table}/{id}")]
    [资源按路由("table", "档案")]
    [需要权限("EDIT")]
    public async Task<IActionResult> 修改(string table, string id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (校验(table) is { } 拒绝) return 拒绝;
        var m = 元数据.取(table);
        var n = await _data.修改Async(table, 转换主键(m, id), ToDict(body), ct);
        return n > 0 ? Ok(new { ok = true }) : NotFound(new { message = "记录不存在" });
    }

    [HttpDelete("{table}/{id}")]
    [资源按路由("table", "档案")]
    [需要权限("DEL")]
    public async Task<IActionResult> 删除(string table, string id, CancellationToken ct)
    {
        if (校验(table) is { } 拒绝) return 拒绝;
        var m = 元数据.取(table);
        try
        {
            var n = await _data.删除Async(table, 转换主键(m, id), ct);
            return n > 0 ? Ok(new { ok = true }) : NotFound(new { message = "记录不存在" });
        }
        catch (Npgsql.PostgresException)
        {
            // 外键约束：档案被单据引用时不允许删除
            return Conflict(new { message = "该记录已被其他单据引用，不能删除" });
        }
    }

    private static object 转换主键(表元数据 m, string id) =>
        m.取列(m.主键)?.类型 is "INTEGER" or "COUNTER" ? int.Parse(id) : id;

    private static Dictionary<string, object?> ToDict(JsonElement body)
    {
        var d = new Dictionary<string, object?>();
        if (body.ValueKind != JsonValueKind.Object) return d;
        foreach (var p in body.EnumerateObject())
        {
            d[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.Number => p.Value.TryGetInt64(out var l) ? l : p.Value.GetDecimal(),
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                JsonValueKind.Null => null,
                _ => p.Value.ToString()
            };
        }
        return d;
    }
}
