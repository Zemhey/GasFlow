using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>结算方式（对应数据库表 dbo.结算方式）</summary>
[Table("结算方式")]
public class 结算方式
{
    [Key] [Column("结算方式编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 结算方式编号 { get; set; }
    [Column("结算方式编码")] [MaxLength(20)]
    public string? 结算方式编码 { get; set; }
    [Column("结算方式名称")] [MaxLength(50)]
    public string? 结算方式名称 { get; set; }
    [Column("说明")] [MaxLength(255)]
    public string? 说明 { get; set; }
    [Column("是否有效")]
    public bool 是否有效 { get; set; }
}
