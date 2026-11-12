using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>还瓶单明细（对应数据库表 dbo.还瓶单明细）</summary>
[Table("还瓶单明细")]
public class 还瓶单明细
{
    [Key] [Column("还瓶明细编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 还瓶明细编号 { get; set; }
    [Column("还瓶单编号")]
    public int? 还瓶单编号 { get; set; }
    [Column("规格编号")]
    public int? 规格编号 { get; set; }
    [Column("归还数量")]
    public int? 归还数量 { get; set; }
    [Column("钢瓶单价")]
    public decimal? 钢瓶单价 { get; set; }
}
