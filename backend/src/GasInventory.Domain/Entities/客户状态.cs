using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>客户状态（对应数据库表 dbo.客户状态）</summary>
[Table("客户状态")]
public class 客户状态
{
    [Key] [Column("客户状态编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 客户状态编号 { get; set; }
    [Column("状态编码")] [MaxLength(20)]
    public string? 状态编码 { get; set; }
    [Column("状态名称")] [MaxLength(50)]
    public string? 状态名称 { get; set; }
    [Column("状态描述")] [MaxLength(255)]
    public string? 状态描述 { get; set; }
    [Column("是否有效")]
    public bool 是否有效 { get; set; }
}
