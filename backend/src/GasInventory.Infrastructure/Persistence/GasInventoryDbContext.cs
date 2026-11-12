using GasInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GasInventory.Infrastructure.Persistence;

/// <summary>
/// 进销存数据库上下文。
/// 表结构由 db/00~03 的 T-SQL 脚本创建（单一来源），本上下文只做映射，不参与建表。
/// 实体与列名均为中文，一一对应，避免 251 个字段的人工英译映射。
/// </summary>
public partial class GasInventoryDbContext : DbContext
{
    public GasInventoryDbContext(DbContextOptions<GasInventoryDbContext> options) : base(options) { }

    public DbSet<付款方式> 付款方式 => Set<付款方式>();
    public DbSet<供气商> 供气商 => Set<供气商>();
    public DbSet<借瓶单> 借瓶单 => Set<借瓶单>();
    public DbSet<借瓶单明细> 借瓶单明细 => Set<借瓶单明细>();
    public DbSet<入仓单> 入仓单 => Set<入仓单>();
    public DbSet<入仓单明细> 入仓单明细 => Set<入仓单明细>();
    public DbSet<出货与送气分类> 出货与送气分类 => Set<出货与送气分类>();
    public DbSet<员工> 员工 => Set<员工>();
    /// <summary>v3 用 员工兼岗（复合主键）取代 v2 的 员工岗位</summary>
    public DbSet<员工兼岗> 员工兼岗 => Set<员工兼岗>();
    public DbSet<客户> 客户 => Set<客户>();
    public DbSet<客户分类> 客户分类 => Set<客户分类>();
    public DbSet<客户区域> 客户区域 => Set<客户区域>();
    public DbSet<客户提成标准> 客户提成标准 => Set<客户提成标准>();
    public DbSet<客户状态> 客户状态 => Set<客户状态>();
    public DbSet<岗位> 岗位 => Set<岗位>();
    public DbSet<岗位权限> 岗位权限 => Set<岗位权限>();
    public DbSet<打气车> 打气车 => Set<打气车>();
    public DbSet<押金收据> 押金收据 => Set<押金收据>();
    public DbSet<权限矩阵缓存> 权限矩阵缓存 => Set<权限矩阵缓存>();
    public DbSet<权限资源> 权限资源 => Set<权限资源>();
    public DbSet<权限项> 权限项 => Set<权限项>();
    public DbSet<每日盘点> 每日盘点 => Set<每日盘点>();
    public DbSet<每日盘点明细> 每日盘点明细 => Set<每日盘点明细>();
    public DbSet<登录记录> 登录记录 => Set<登录记录>();
    public DbSet<会话> 会话 => Set<会话>();
    public DbSet<部门> 部门 => Set<部门>();
    public DbSet<系统参数> 系统参数 => Set<系统参数>();
    public DbSet<结算方式> 结算方式 => Set<结算方式>();
    public DbSet<结算账期> 结算账期 => Set<结算账期>();
    public DbSet<计提方案> 计提方案 => Set<计提方案>();
    public DbSet<计提方案明细> 计提方案明细 => Set<计提方案明细>();
    public DbSet<过磅单> 过磅单 => Set<过磅单>();
    public DbSet<运气表> 运气表 => Set<运气表>();
    public DbSet<还瓶单> 还瓶单 => Set<还瓶单>();
    public DbSet<还瓶单明细> 还瓶单明细 => Set<还瓶单明细>();
    public DbSet<进气单主干> 进气单主干 => Set<进气单主干>();
    public DbSet<进气单明细> 进气单明细 => Set<进气单明细>();
    public DbSet<送气单> 送气单 => Set<送气单>();
    public DbSet<送气单明细> 送气单明细 => Set<送气单明细>();
    public DbSet<钢瓶规格> 钢瓶规格 => Set<钢瓶规格>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 复合主键（v3 改过的两张关联表）——EF 默认只认名为 Id/类名+Id 的键，这里显式声明
        modelBuilder.Entity<岗位权限>().HasKey(x => new { x.岗位编号, x.资源编号, x.权限编号 });
        modelBuilder.Entity<员工兼岗>().HasKey(x => new { x.员工编号, x.岗位编号 });

        // 金额(19,4) / 公斤(18,3) 等精度，见 GasInventoryDbContext.精度.cs（生成的代码）
        应用精度(modelBuilder);

        // 日期统一 datetime2(0)；遗漏精度的小数按金额口径兜底
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                if (prop.ClrType == typeof(decimal) || prop.ClrType == typeof(decimal?))
                {
                    if (prop.GetPrecision() is null)
                    {
                        prop.SetPrecision(19);
                        prop.SetScale(4);
                    }
                }
                if (prop.ClrType == typeof(DateTime) || prop.ClrType == typeof(DateTime?))
                {
                    prop.SetColumnType("timestamp(0)");
                }
            }
        }
    }
}
