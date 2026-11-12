using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>付款方式（对应数据库表 dbo.付款方式）</summary>
[Table("付款方式")]
public class 付款方式
{
    [Key] [Column("付款方式编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 付款方式编号 { get; set; }
    [Column("付款方式编码")] [MaxLength(20)]
    public string? 付款方式编码 { get; set; }
    [Column("付款方式名称")] [MaxLength(50)]
    public string? 付款方式名称 { get; set; }
    [Column("结算方式编号")]
    public int? 结算方式编号 { get; set; }
    [Column("是否有效")]
    public bool 是否有效 { get; set; }
}
