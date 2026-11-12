using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>借瓶单（对应数据库表 dbo.借瓶单）</summary>
[Table("借瓶单")]
public class 借瓶单
{
    [Key] [Column("借瓶单编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 借瓶单编号 { get; set; }
    [Column("单据号码")] [MaxLength(30)]
    public string? 单据号码 { get; set; }
    [Column("借瓶日期")]
    public DateTime? 借瓶日期 { get; set; }
    [Column("客户编号")]
    public int? 客户编号 { get; set; }
    [Column("经办员工编号")]
    public int? 经办员工编号 { get; set; }
    [Column("应还日期")]
    public DateTime? 应还日期 { get; set; }
    [Column("备注")]
    public string? 备注 { get; set; }
}
