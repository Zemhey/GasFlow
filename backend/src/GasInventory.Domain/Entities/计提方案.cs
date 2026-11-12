using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>计提方案（对应数据库表 dbo.计提方案）</summary>
[Table("计提方案")]
public class 计提方案
{
    [Key] [Column("计提方案编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 计提方案编号 { get; set; }
    [Column("方案编码")] [MaxLength(20)]
    public string? 方案编码 { get; set; }
    [Column("方案名称")] [MaxLength(50)]
    public string? 方案名称 { get; set; }
    [Column("生效日期")]
    public DateTime? 生效日期 { get; set; }
    [Column("失效日期")]
    public DateTime? 失效日期 { get; set; }
    [Column("是否在用")]
    public bool 是否在用 { get; set; }
    [Column("备注")]
    public string? 备注 { get; set; }
}
