using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>每日盘点明细（对应数据库表 dbo.每日盘点明细）</summary>
[Table("每日盘点明细")]
public class 每日盘点明细
{
    [Key] [Column("盘点明细编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 盘点明细编号 { get; set; }
    [Column("盘点编号")]
    public int? 盘点编号 { get; set; }
    [Column("规格编号")]
    public int? 规格编号 { get; set; }
    [Column("实瓶数量")]
    public int? 实瓶数量 { get; set; }
    [Column("空瓶数量")]
    public int? 空瓶数量 { get; set; }
    [Column("钢瓶总数")]
    public int? 钢瓶总数 { get; set; }
}
