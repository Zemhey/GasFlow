using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>客户分类（对应数据库表 dbo.客户分类）</summary>
[Table("客户分类")]
public class 客户分类
{
    [Key] [Column("客户分类编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 客户分类编号 { get; set; }
    [Column("分类编码")] [MaxLength(20)]
    public string? 分类编码 { get; set; }
    [Column("分类名称")] [MaxLength(50)]
    public string? 分类名称 { get; set; }
    [Column("分类描述")] [MaxLength(255)]
    public string? 分类描述 { get; set; }
    [Column("是否有效")]
    public bool 是否有效 { get; set; }
}
