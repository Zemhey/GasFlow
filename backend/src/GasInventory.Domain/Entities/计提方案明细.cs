using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>计提方案明细（对应数据库表 dbo.计提方案明细）</summary>
[Table("计提方案明细")]
public class 计提方案明细
{
    [Key] [Column("计提明细编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 计提明细编号 { get; set; }
    [Column("计提方案编号")]
    public int? 计提方案编号 { get; set; }
    [Column("规格编号")]
    public int? 规格编号 { get; set; }
    [Column("提成金额")]
    public decimal? 提成金额 { get; set; }
}
