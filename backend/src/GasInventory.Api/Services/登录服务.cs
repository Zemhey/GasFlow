using System.Security.Cryptography;
using System.Text;
using GasInventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GasInventory.Api.Services;

/// <summary>
/// 密码算法（v3 口径）：PBKDF2-HMAC-SHA256，逐员工独立盐，迭代次数落在 员工.密码迭代次数。
/// 派生值 32 字节存 员工.密码哈希，盐 16 字节存 员工.密码盐。
///
/// 为什么不再用 MD5：v2 存的是未加盐的 32 位小写 MD5，彩虹表可反查；
/// v3 的 CHECK 约束 CK_员工_密码完整性 也要求 哈希/盐/迭代次数 同时为空或同时非空。
/// 与库里的约束一致：迭代次数必须落在 10000 ~ 1000000。
/// </summary>
public static class 密码算法
{
    /// <summary>迭代次数下限（与 CK_员工_密码完整性 一致）</summary>
    public const int 最小迭代 = 10000;
    /// <summary>默认迭代次数（PBKDF2-SHA256，约 50 ms/次，兼顾安全与登录响应）</summary>
    public const int 默认迭代 = 100000;
    /// <summary>盐长度（字节）</summary>
    public const int 盐长度 = 16;
    /// <summary>派生值长度（字节），对应 密码哈希 varbinary(64)</summary>
    public const int 哈希长度 = 32;

    public static byte[] 新盐() => RandomNumberGenerator.GetBytes(盐长度);

    public static byte[] 派生(string 密码, byte[] 盐, int 迭代次数) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(密码 ?? ""), 盐,
            Math.Clamp(迭代次数, 最小迭代, 1_000_000),
            HashAlgorithmName.SHA256, 哈希长度);

    /// <summary>固定时间比较，避免按字节提前返回泄漏信息</summary>
    public static bool 校验(string 密码, byte[]? 盐, int? 迭代次数, byte[]? 哈希)
    {
        if (盐 is null || 哈希 is null || 迭代次数 is null) return false;
        if (盐.Length == 0 || 哈希.Length == 0) return false;
        var 计算 = 派生(密码, 盐, 迭代次数.Value);
        return CryptographicOperations.FixedTimeEquals(计算, 哈希);
    }
}

/// <summary>
/// 登录策略服务：账号锁定、失败计数、初始密码判定与密码重置。
/// 口径来自 系统参数 表（sys.登录失败锁定次数 / sys.登录锁定分钟 / sys.初始密码），
/// 不在代码里写死 —— 改参数即改策略。
/// </summary>
public class 登录服务
{
    private readonly GasInventoryDbContext _db;

    public 登录服务(GasInventoryDbContext db) => _db = db;

    private static readonly Dictionary<string, string> 缺省参数 = new()
    {
        ["sys.登录失败锁定次数"] = "5",
        ["sys.登录锁定分钟"] = "30",
        ["sys.初始密码"] = "123456"
    };

    /// <summary>读参数；库里没有或值非法时用缺省值（参数表是配置，不该因为少一行就登录不了）</summary>
    public async Task<string> 参数Async(string 名称, CancellationToken ct = default)
    {
        var v = await _db.系统参数.AsNoTracking()
            .Where(x => x.参数名 == 名称).Select(x => x.参数值).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(v) ? 缺省参数.GetValueOrDefault(名称, "") : v!.Trim();
    }

    public async Task<int> 失败锁定次数Async(CancellationToken ct = default) =>
        int.TryParse(await 参数Async("sys.登录失败锁定次数", ct), out var n) && n > 0 ? n : 5;

    public async Task<int> 锁定分钟Async(CancellationToken ct = default) =>
        int.TryParse(await 参数Async("sys.登录锁定分钟", ct), out var n) && n > 0 ? n : 30;

    /// <summary>是否为系统初始密码（员工从未改过密码且哈希恰为初始密码派生值时才算）</summary>
    public async Task<bool> 是初始密码Async(string 密码, CancellationToken ct = default) =>
        string.Equals(密码, await 参数Async("sys.初始密码", ct), StringComparison.Ordinal);

    /// <summary>给员工设置新密码（只改内存对象，由调用方 SaveChanges）</summary>
    public static void 设密码(Domain.Entities.员工 e, string 密码, int 迭代次数 = 密码算法.默认迭代)
    {
        var 盐 = 密码算法.新盐();
        e.密码盐 = 盐;
        e.密码迭代次数 = 迭代次数;
        e.密码哈希 = 密码算法.派生(密码, 盐, 迭代次数);
        e.失败登录次数 = 0;
        e.锁定至 = null;
    }

    /// <summary>
    /// 首次部署：把"从未开通登录"（密码哈希为空）的在职员工，按初始密码开通一次。
    /// 返回实际开通的人数。只补加密钥为空的人，不会覆盖任何已经改过的密码。
    /// </summary>
    public async Task<int> 初始化密码Async(CancellationToken ct = default)
    {
        var 初始密码 = await 参数Async("sys.初始密码", ct);
        if (初始密码.Length < 6) return 0;

        var 待开通 = await _db.员工.AsNoTracking()
            .Where(e => e.密码哈希 == null && e.密码盐 == null && e.密码迭代次数 == null)
            .Select(e => new { e.员工编号, e.工号 })
            .ToListAsync(ct);
        if (待开通.Count == 0) return 0;

        foreach (var x in 待开通)
        {
            var 盐 = 密码算法.新盐();
            var 哈希 = 密码算法.派生(初始密码, 盐, 密码算法.默认迭代);
            // 用 SQL 直更，绕开 EF 对 rowversion/审计列的跟踪开销；条件里再次确认"仍未开通"，防并发重复写
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "员工" SET "密码哈希" = {哈希}, "密码盐" = {盐},
                       "密码迭代次数" = {密码算法.默认迭代},
                       "初始密码是否已改" = false, "失败登录次数" = 0
                WHERE "员工编号" = {x.员工编号} AND "密码哈希" IS NULL
                """, ct);
        }
        return 待开通.Count;
    }
}
