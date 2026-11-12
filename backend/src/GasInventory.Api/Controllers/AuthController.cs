using System.Security.Claims;
using GasInventory.Api.Services;
using GasInventory.Domain.Entities;
using GasInventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GasInventory.Api.Controllers;

/// <summary>
/// 认证：登录 / 登出 / 当前用户 / 改密 / 菜单。
/// v3 口径（2026-10 升 v3 时改）：密码不再是明文 MD5 列，而是 PBKDF2-SHA256 + 员工独立盐，
/// 迭代次数落 员工.密码迭代次数；岗位改叫 主岗位编号，兼岗在 员工兼岗。
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly GasInventoryDbContext _db;
    private readonly 权限服务 _perm;
    private readonly 登录服务 _login;

    public AuthController(GasInventoryDbContext db, 权限服务 perm, 登录服务 login)
    {
        _db = db;
        _perm = perm;
        _login = login;
    }

    public sealed record 登录入参(string 工号, string 密码);
    public sealed record 改密入参(string 旧密码, string 新密码);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> 登录([FromBody] 登录入参 body, CancellationToken ct = default)
    {
        var 工号 = body.工号?.Trim() ?? "";
        var 密码 = body.密码 ?? "";
        var ip = Ip();

        if (工号.Length == 0 || 密码.Length == 0)
            return Unauthorized(new { message = "请输入工号和密码" });

        var e = await _db.员工.AsNoTracking().FirstOrDefaultAsync(x => x.工号 == 工号, ct);

        // 账号不存在：记"账号不存在"，但不暴露给前端（同样回"工号或密码不正确"）
        if (e is null)
        {
            await 写登录记录(工号, null, "失败", "账号不存在", "工号不存在", ip, ct);
            return Unauthorized(new { message = "工号或密码不正确" });
        }

        var 现在是 = DateTime.Now;

        // 1) 账号锁定：锁定至未到 → 直接拒绝（不计入失败次数，否则永远解不开）
        if (e.锁定至 is { } 至 && 至 > 现在是)
        {
            await 写登录记录(e.工号, e.员工编号, "锁定", "账号锁定",
                $"锁定至 {至:yyyy-MM-dd HH:mm}", ip, ct);
            return StatusCode(StatusCodes.Status423Locked,
                new { message = $"账号已锁定，请于 {至:HH:mm} 后重试，或联系管理员解锁" });
        }

        // 2) 停用
        if (!e.是否在职)
        {
            await 写登录记录(e.工号, e.员工编号, "失败", "账号停用", "员工已离职/停用", ip, ct);
            return Unauthorized(new { message = "账号已停用，请联系管理员" });
        }

        // 3) 未开通登录（密码字段三列全空）
        if (e.密码哈希 is null || e.密码盐 is null || e.密码迭代次数 is null)
        {
            await 写登录记录(e.工号, e.员工编号, "失败", "其他", "尚未开通登录", ip, ct);
            return Unauthorized(new { message = "该账号尚未开通登录，请联系管理员设置密码" });
        }

        // 4) 校验口令
        if (!密码算法.校验(密码, e.密码盐, e.密码迭代次数, e.密码哈希))
        {
            // 计数直接取库里的当前值再 +1（不用 EF 的跟踪值，避免本进程内旧快照覆盖别处写入）
            var 已失败 = await _db.员工.AsNoTracking()
                .Where(x => x.员工编号 == e.员工编号).Select(x => x.失败登录次数)
                .FirstOrDefaultAsync(ct) + 1;
            var 上限 = await _login.失败锁定次数Async(ct);
            DateTime? 锁定至 = null;
            var 剩余 = 已失败;
            if (已失败 >= 上限)
            {
                锁定至 = 现在是.AddMinutes(await _login.锁定分钟Async(ct));
                剩余 = 0;   // 锁定期内不再累加，解锁后从 0 开始
            }
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"员工\" SET \"失败登录次数\" = {剩余}, \"锁定至\" = {锁定至} WHERE \"员工编号\" = {e.员工编号}", ct);

            await 写登录记录(e.工号, e.员工编号, 锁定至 is null ? "失败" : "锁定", "密码错误",
                锁定至 is null ? $"密码不正确（第 {剩余}/{上限} 次）"
                               : $"密码不正确，连续 {上限} 次，锁定至 {锁定至:yyyy-MM-dd HH:mm}",
                ip, ct);

            return Unauthorized(new
            {
                message = 锁定至 is null
                    ? $"工号或密码不正确（还可尝试 {Math.Max(0, 上限 - 剩余)} 次）"
                    : $"密码连续错误 {上限} 次，账号已锁定 {await _login.锁定分钟Async(ct)} 分钟"
            });
        }

        // 6) 通过：清失败计数、记最后登录时间
        // 用 SQL 直更而不是 EF 跟踪：登录是每次都要走的高频路径，
        // 回读不依赖 rowversion（会话表也有触发器，EF 的 OUTPUT 回读会踩 334）。
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE "员工"
                SET "失败登录次数" = 0, "锁定至" = NULL, "最后登录时间" = now()
              WHERE "员工编号" = {e.员工编号}
             """, ct);

        var 登录记录编号 = await 写登录记录(e.工号, e.员工编号, "成功", null, null, ip, ct);
        var 会话编号 = await 建会话Async(e.员工编号, ip, ct);
        if (登录记录编号 is { } lr && 会话编号 is { } sn)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"登录记录\" SET \"会话编号\" = {sn} WHERE \"登录记录编号\" = {lr}", ct);
        }

        var claims = new List<Claim>
        {
            new("员工编号", e.员工编号.ToString()),
            new(ClaimTypes.Name, e.姓名 ?? ""),
            new("工号", e.工号 ?? ""),
            new("姓名", e.姓名 ?? ""),
            new("岗位编号", (e.主岗位编号 ?? 0).ToString()),
            new("会话编号", (会话编号 ?? 0).ToString())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false });

        return Ok(await 当前用户信息(e));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> 登出(CancellationToken ct = default)
    {
        var 会话编号 = int.TryParse(User.FindFirst("会话编号")?.Value, out var sn) ? sn : 0;
        var 员工编号 = int.TryParse(User.FindFirst("员工编号")?.Value, out var id) ? id : 0;

        if (会话编号 > 0)
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE "会话" SET "状态" = '离线', "最后活动时间" = now()
                 WHERE "会话编号" = {会话编号} AND "状态" = '在线'
                 """, ct);
        if (员工编号 > 0)
            await 写登录记录(User.FindFirst("工号")?.Value, 员工编号, "登出", null, null, Ip(), ct);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { ok = true });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> 当前用户(CancellationToken ct = default)
    {
        if (!int.TryParse(User.FindFirst("员工编号")?.Value, out var id))
            return Unauthorized(new { message = "会话已失效，请重新登录" });

        var e = await _db.员工.AsNoTracking().FirstOrDefaultAsync(x => x.员工编号 == id, ct);
        if (e is null) return Unauthorized(new { message = "账号不存在" });
        return Ok(await 当前用户信息(e));
    }

    /// <summary>
    /// 菜单：把"当前员工有权访问的权限资源"按前端契约 { 资源, 名称, 路由, 模块, 排序号 } 下发。
    /// 路由空串表示"有资源但前端还没有对应页面"，前端按自身菜单表过滤，不会渲染死链。
    /// </summary>
    [HttpGet("menus")]
    [Authorize]
    public async Task<IActionResult> 菜单(CancellationToken ct = default)
    {
        if (!int.TryParse(User.FindFirst("员工编号")?.Value, out var id))
            return Unauthorized(new { message = "会话已失效，请重新登录" });

        var 对象名 = await _perm.资源清单Async(id);
        var 资源 = await _db.权限资源.AsNoTracking()
            .Where(r => r.是否启用 && r.对象名 != null && 对象名.Contains(r.对象名))
            .OrderBy(r => r.排序号).ThenBy(r => r.资源编号)
            .Select(r => new { r.资源代码, r.资源名称, r.模块, r.对象名, r.排序号 })
            .ToListAsync(ct);

        return Ok(new
        {
            菜单 = 资源.Select(r => new
            {
                资源 = r.对象名,
                名称 = r.资源名称,
                路由 = "",
                模块 = r.模块,
                排序号 = r.排序号
            }),
            // 前端路由 → 资源对象名：前端据此做页面级资源校验（不在这里就等于前端裸奔）
            路由 = 路由资源表.全部
        });
    }

    [HttpPost("password")]
    [Authorize]
    public async Task<IActionResult> 修改密码([FromBody] 改密入参 body, CancellationToken ct = default)
    {
        var 新密码 = body.新密码 ?? "";
        if (新密码.Length < 6)
            return BadRequest(new { message = "新密码至少 6 位" });
        if (string.Equals(新密码, body.旧密码, StringComparison.Ordinal))
            return BadRequest(new { message = "新密码不能与原密码相同" });
        if (await _login.是初始密码Async(新密码, ct))
            return BadRequest(new { message = "新密码不能使用系统初始密码" });

        if (!int.TryParse(User.FindFirst("员工编号")?.Value, out var id))
            return Unauthorized(new { message = "会话已失效，请重新登录" });

        var e = await _db.员工.FirstOrDefaultAsync(x => x.员工编号 == id, ct);
        if (e is null) return Unauthorized(new { message = "账号不存在" });

        if (!密码算法.校验(body.旧密码 ?? "", e.密码盐, e.密码迭代次数, e.密码哈希))
            return BadRequest(new { message = "原密码不正确" });

        登录服务.设密码(e, 新密码);
        e.初始密码是否已改 = true;
        // 只改密码相关的几列（不动 rowversion/审计列，也不触发 EF 的 OUTPUT 回读）
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE "员工"
                SET "密码哈希" = {e.密码哈希}, "密码盐" = {e.密码盐}, "密码迭代次数" = {e.密码迭代次数},
                    "初始密码是否已改" = true, "失败登录次数" = 0, "锁定至" = NULL,
                    "更新时间" = now()
              WHERE "员工编号" = {e.员工编号}
             """, ct);

        return Ok(new { ok = true });
    }

    /// <summary>统一返回体（前端 登录结果 契约）：员工编号/工号/姓名/部门名称/岗位名称/必须改密/资源/权限</summary>
    private async Task<object> 当前用户信息(员工 e)
    {
        var 部门名 = "";
        if (e.部门编号 is > 0)
        {
            部门名 = await _db.部门.AsNoTracking()
                .Where(d => d.部门编号 == e.部门编号)
                .Select(d => d.部门名称).FirstOrDefaultAsync() ?? "";
        }

        var 岗位名 = "";
        if (e.主岗位编号 is > 0)
        {
            岗位名 = await _db.岗位.AsNoTracking()
                .Where(p => p.岗位编号 == e.主岗位编号)
                .Select(p => p.岗位名称).FirstOrDefaultAsync() ?? "";
        }

        return new
        {
            e.员工编号,
            e.工号,
            e.姓名,
            部门名称 = 部门名,
            岗位名称 = 岗位名,
            岗位编号 = e.主岗位编号,
            必须改密 = !e.初始密码是否已改,
            资源 = await _perm.资源清单Async(e.员工编号),
            权限 = await _perm.权限表Async(e.员工编号)
        };
    }

    /// <summary>写登录记录（返回记录编号，用于回填会话编号）</summary>
    private async Task<long?> 写登录记录(string? 工号, int? 员工编号, string 结果, string? 失败分类,
        string? 失败原因, string ip, CancellationToken ct)
    {
        var ua = Request.Headers.UserAgent.ToString();
        var rec = new 登录记录
        {
            工号或账号 = 截断(工号, 40),
            员工编号 = 员工编号,
            登录时间 = DateTime.Now,
            登录结果 = 结果,
            失败原因分类 = 失败分类,
            失败原因 = 截断(失败原因, 400),
            登录方式 = "密码",
            客户端 = "Web",
            客户端IP = 截断(ip, 64),
            浏览器 = 截断(浏览器名(ua), 80),
            UserAgent = 截断(ua, 400)
        };
        await _db.登录记录.AddAsync(rec, ct);
        await _db.SaveChangesAsync(ct);
        return rec.登录记录编号;
    }

    private async Task<int?> 建会话Async(int 员工编号, string ip, CancellationToken ct)
    {
        try
        {
            var 会话ID = Guid.NewGuid().ToString("N");
            // 会话表带留痕触发器：用 INSERT + OUTPUT ... INTO 回读主键（裸 OUTPUT 会被 334 拒）
            var 编号 = new List<int>();
            var 参数 = new[]
            {
                new NpgsqlParameter("@id", 会话ID),
                new NpgsqlParameter("@e", 员工编号),
                new NpgsqlParameter("@ip", 截断(ip, 64) ?? (object)DBNull.Value)
            };
            await using var cmd = _db.Database.GetDbConnection().CreateCommand();
            if (cmd.Connection!.State != System.Data.ConnectionState.Open)
                await cmd.Connection.OpenAsync(ct);
            cmd.CommandText = """
                INSERT INTO "会话" ("会话ID", "员工编号", "登录时间", "最后活动时间", "过期时间", "客户端IP", "客户端", "状态")
                VALUES (@id, @e, now(), now(), now() + interval '12 hours', @ip, 'Web', '在线')
                RETURNING "会话编号";
                """;
            cmd.Parameters.AddRange(参数);
            var v = await cmd.ExecuteScalarAsync(ct);
            return v is null or DBNull ? null : Convert.ToInt32(v);
        }
        catch (Exception)
        {
            // 会话表写失败不该让人登不进来：认证态在 Cookie 里，会话只是审计增强
            return null;
        }
    }

    private static string? 截断(string? s, int n) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= n ? s : s[..n]);

    private static string 浏览器名(string ua)
    {
        if (string.IsNullOrEmpty(ua)) return "";
        if (ua.Contains("Edg/")) return "Edge";
        if (ua.Contains("Chrome/")) return "Chrome";
        if (ua.Contains("Firefox/")) return "Firefox";
        if (ua.Contains("Safari/")) return "Safari";
        return ua.Length <= 40 ? ua : ua[..40];
    }

    private string Ip()
    {
        var 转发 = Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(转发))
        {
            var first = 转发.Split(',')[0].Trim();
            if (first.Length > 0) return first;
        }
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "本机";
        return ip;
    }
}
