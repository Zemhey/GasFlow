using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>结算账期（对应数据库表 dbo.结算账期）</summary>
[Table("结算账期")]
public class 结算账期
{
    [Key] [Column("结算账期编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 结算账期编号 { get; set; }
    [Column("账期编码")] [MaxLength(20)]
    public string? 账期编码 { get; set; }
    [Column("账期名称")] [MaxLength(50)]
    public string? 账期名称 { get; set; }
    [Column("账期天数")]
    public int? 账期天数 { get; set; }
    [Column("是否有效")]
    public bool 是否有效 { get; set; }
}
