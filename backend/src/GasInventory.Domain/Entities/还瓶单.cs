using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>还瓶单（对应数据库表 dbo.还瓶单）</summary>
[Table("还瓶单")]
public class 还瓶单
{
    [Key] [Column("还瓶单编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 还瓶单编号 { get; set; }
    [Column("单据号码")] [MaxLength(30)]
    public string? 单据号码 { get; set; }
    [Column("还瓶日期")]
    public DateTime? 还瓶日期 { get; set; }
    [Column("客户编号")]
    public int? 客户编号 { get; set; }
    [Column("经办员工编号")]
    public int? 经办员工编号 { get; set; }
    [Column("备注")]
    public string? 备注 { get; set; }
}
