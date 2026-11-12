using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>借瓶单明细（对应数据库表 dbo.借瓶单明细）</summary>
[Table("借瓶单明细")]
public class 借瓶单明细
{
    [Key] [Column("借瓶明细编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 借瓶明细编号 { get; set; }
    [Column("借瓶单编号")]
    public int? 借瓶单编号 { get; set; }
    [Column("规格编号")]
    public int? 规格编号 { get; set; }
    [Column("临时借瓶数量")]
    public int? 临时借瓶数量 { get; set; }
    [Column("长期借瓶数量")]
    public int? 长期借瓶数量 { get; set; }
    [Column("押金单价")]
    public decimal? 押金单价 { get; set; }
}
