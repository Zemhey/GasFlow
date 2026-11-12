using GasInventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GasInventory.Api.Services;

/// <summary>
/// 权限服务：岗位 × 资源(页面) × 权限项(查看/新增/修改/删除/审核/过账/打印/导出)。
/// 数据来源：岗位权限 表（种子里的 692 条基线授权）。
/// 员工岗位解析（v3）：主岗 = 员工.主岗位编号，兼岗 = 员工兼岗（有效期内）；
/// 超级管理员不再写死"岗位编号 == 1"，改读 岗位.是否超级管理员。
/// </summary>
public class 权限服务
{
    private readonly GasInventoryDbContext _db;

    public 权限服务(GasInventoryDbContext db) => _db = db;

    /// <summary>员工的全部岗位编号：主岗位 + 仍在有效期内的兼岗（去重、去空）</summary>
    public async Task<List<int>> 岗位集合Async(int 员工编号)
    {
        if (员工编号 <= 0) return new List<int>();

        var 主岗 = await _db.员工.AsNoTracking()
            .Where(e => e.员工编号 == 员工编号)
            .Select(e => e.主岗位编号)
            .FirstOrDefaultAsync();

        var 今天 = DateOnly.FromDateTime(DateTime.Today);
        var 兼岗 = await _db.员工兼岗.AsNoTracking()
            .Where(x => x.员工编号 == 员工编号
                        && (x.生效日期 == null || x.生效日期 <= 今天)
                        && (x.失效日期 == null || x.失效日期 >= 今天))
            .Select(x => x.岗位编号)
            .ToListAsync();

        var ids = new List<int>();
        if (主岗 is > 0) ids.Add(主岗.Value);
        ids.AddRange(兼岗.Where(x => x > 0));
        return ids.Distinct().ToList();
    }

    /// <summary>是否超级管理员岗位（岗位.是否超级管理员 = 1）</summary>
    public async Task<bool> 是超级管理员Async(int 员工编号)
    {
        var ids = await 岗位集合Async(员工编号);
        if (ids.Count == 0) return false;
        return await _db.岗位.AsNoTracking()
            .AnyAsync(p => ids.Contains(p.岗位编号) && p.是否超级管理员);
    }

    public async Task<bool> 有权限Async(int 员工编号, string 资源对象名, string 权限代码)
    {
        var ids = await 岗位集合Async(员工编号);
        if (ids.Count == 0) return false;

        if (await 是超级管理员Async(员工编号)) return true;

        return await _db.岗位权限.AsNoTracking()
            .Where(p => ids.Contains(p.岗位编号))
            .Join(_db.权限资源.AsNoTracking(), p => p.资源编号, r => r.资源编号, (p, r) => new { p, r })
            .Join(_db.权限项.AsNoTracking(), x => x.p.权限编号, q => q.权限编号, (x, q) => new { x.r, q })
            .AnyAsync(x => x.r.对象名 == 资源对象名 && x.q.权限代码.ToLower() == 权限代码.ToLower());
    }

    /// <summary>当前员工可访问的资源对象名集合（前端据此渲染菜单）</summary>
    public async Task<List<string>> 资源清单Async(int 员工编号)
    {
        var ids = await 岗位集合Async(员工编号);
        if (ids.Count == 0) return new List<string>();

        if (await 是超级管理员Async(员工编号))
            return await _db.权限资源.AsNoTracking()
                .Where(r => r.是否启用 && r.对象名 != null)
                .OrderBy(r => r.排序号)
                .Select(r => r.对象名!)
                .ToListAsync();

        return await _db.岗位权限.AsNoTracking()
            .Where(p => ids.Contains(p.岗位编号))
            .Join(_db.权限资源.AsNoTracking(), p => p.资源编号, r => r.资源编号, (p, r) => r)
            .Where(r => r.是否启用 && r.对象名 != null)
            .Select(r => r.对象名!)
            .Distinct()
            .ToListAsync();
    }

    /// <summary>
    /// 资源 → 权限代码 集合（前端 有权限(资源, 代码) 用）。
    /// 超级管理员返回 null，前端约定"未下发细粒度权限时只靠资源级"。
    /// </summary>
    public async Task<Dictionary<string, List<string>>?> 权限表Async(int 员工编号)
    {
        var ids = await 岗位集合Async(员工编号);
        if (ids.Count == 0) return new Dictionary<string, List<string>>();
        if (await 是超级管理员Async(员工编号)) return null;

        var rows = await _db.岗位权限.AsNoTracking()
            .Where(p => ids.Contains(p.岗位编号))
            .Join(_db.权限资源.AsNoTracking(), p => p.资源编号, r => r.资源编号, (p, r) => new { r, p })
            .Join(_db.权限项.AsNoTracking(), x => x.p.权限编号, q => q.权限编号, (x, q) => new { x.r.对象名, q.权限代码 })
            .Where(x => x.对象名 != null)
            .ToListAsync();

        return rows
            .GroupBy(x => x.对象名!)
            .ToDictionary(g => g.Key, g => g.Select(x => x.权限代码 ?? "").Where(c => c.Length > 0).Distinct().ToList());
    }
}
