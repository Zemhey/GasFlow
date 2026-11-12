using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>进气单明细（对应数据库表 dbo.进气单明细）</summary>
[Table("进气单明细")]
public class 进气单明细
{
    [Key] [Column("进气明细编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 进气明细编号 { get; set; }
    [Column("进气单编号")]
    public int? 进气单编号 { get; set; }
    [Column("钢瓶规格编号")]
    public int? 钢瓶规格编号 { get; set; }
    [Column("实瓶数量")]
    public int? 实瓶数量 { get; set; }
    [Column("单瓶气重公斤")]
    public decimal? 单瓶气重公斤 { get; set; }
    [Column("单瓶合算价格")]
    public decimal? 单瓶合算价格 { get; set; }
    [Column("门市分配")]
    public int? 门市分配 { get; set; }
}
