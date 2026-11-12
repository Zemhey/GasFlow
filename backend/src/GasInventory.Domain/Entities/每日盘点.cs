using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>每日盘点（对应数据库表 dbo.每日盘点）</summary>
[Table("每日盘点")]
public class 每日盘点
{
    [Key] [Column("盘点编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 盘点编号 { get; set; }
    [Column("盘点日期")]
    public DateTime? 盘点日期 { get; set; }
    [Column("盘点区域")] [MaxLength(50)]
    public string? 盘点区域 { get; set; }
    [Column("盘点员工编号")]
    public int? 盘点员工编号 { get; set; }
    [Column("备注")]
    public string? 备注 { get; set; }
}
