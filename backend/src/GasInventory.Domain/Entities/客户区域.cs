using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>客户区域（对应数据库表 dbo.客户区域）</summary>
[Table("客户区域")]
public class 客户区域
{
    [Key] [Column("客户区域编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 客户区域编号 { get; set; }
    [Column("区域编码")] [MaxLength(20)]
    public string? 区域编码 { get; set; }
    [Column("区域名称")] [MaxLength(50)]
    public string? 区域名称 { get; set; }
    [Column("是否有效")]
    public bool 是否有效 { get; set; }
}
