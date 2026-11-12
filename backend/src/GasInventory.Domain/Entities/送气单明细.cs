using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>送气单明细（对应数据库表 dbo.送气单明细）</summary>
[Table("送气单明细")]
public class 送气单明细
{
    [Key] [Column("送气明细编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 送气明细编号 { get; set; }
    [Column("送气单编号")]
    public int? 送气单编号 { get; set; }
    [Column("钢瓶规格编号")]
    public int? 钢瓶规格编号 { get; set; }
    [Column("实瓶数量")]
    public int? 实瓶数量 { get; set; }
    [Column("单位")] [MaxLength(3)]
    public string? 单位 { get; set; }
    [Column("空瓶数量")]
    public int? 空瓶数量 { get; set; }
    [Column("销售单价")]
    public decimal? 销售单价 { get; set; }
    [Column("提成基数")]
    public decimal? 提成基数 { get; set; }
    [Column("备注")]
    public string? 备注 { get; set; }
    [Column("金额")]
    public decimal? 金额 { get; set; }
}
