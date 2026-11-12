using GasInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GasInventory.Infrastructure.Persistence;

/// <summary>
/// 金额/重量列精度（由 db/tools/gen_precision.py 从元数据生成，勿手改）。
/// 金额 decimal(19,4) 元；公斤 decimal(18,3)；其余小数按 Access 原精度。
/// </summary>
public partial class GasInventoryDbContext
{
    private void 应用精度(ModelBuilder m)
    {
        m.Entity<借瓶单明细>(b => { b.Property(x => x.押金单价).HasPrecision(19, 4); });
        m.Entity<客户提成标准>(b => { b.Property(x => x.提成基数).HasPrecision(19, 4); });
        m.Entity<押金收据>(b => { b.Property(x => x.押金单价).HasPrecision(19, 4); b.Property(x => x.收款金额).HasPrecision(19, 4); });
        m.Entity<计提方案明细>(b => { b.Property(x => x.提成金额).HasPrecision(19, 4); });
        m.Entity<过磅单>(b => { b.Property(x => x.总重量公斤).HasPrecision(18, 3); b.Property(x => x.空车重量公斤).HasPrecision(18, 3); b.Property(x => x.净重量公斤).HasPrecision(18, 3); });
        m.Entity<运气表>(b => { b.Property(x => x.配送公里数).HasPrecision(6, 2); });
        m.Entity<还瓶单明细>(b => { b.Property(x => x.钢瓶单价).HasPrecision(19, 4); });
        m.Entity<进气单主干>(b => { b.Property(x => x.空瓶总重公斤).HasPrecision(18, 3); b.Property(x => x.实瓶总重公斤).HasPrecision(18, 3); b.Property(x => x.金额).HasPrecision(19, 4); });
        m.Entity<进气单明细>(b => { b.Property(x => x.单瓶气重公斤).HasPrecision(18, 3); b.Property(x => x.单瓶合算价格).HasPrecision(19, 4); });
        m.Entity<送气单>(b => { b.Property(x => x.销量合计).HasPrecision(18, 3); b.Property(x => x.金额合计).HasPrecision(19, 4); });
        m.Entity<送气单明细>(b => { b.Property(x => x.销售单价).HasPrecision(19, 4); b.Property(x => x.提成基数).HasPrecision(19, 4); b.Property(x => x.金额).HasPrecision(19, 4); });
        m.Entity<钢瓶规格>(b => { b.Property(x => x.容量公斤).HasPrecision(18, 3); b.Property(x => x.钢瓶单价).HasPrecision(19, 4); b.Property(x => x.押金单价).HasPrecision(19, 4); b.Property(x => x.气重).HasPrecision(18, 3); });
    }
}
