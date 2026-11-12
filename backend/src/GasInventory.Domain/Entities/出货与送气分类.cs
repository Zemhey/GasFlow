using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>出货与送气分类（对应数据库表 dbo.出货与送气分类）</summary>
[Table("出货与送气分类")]
public class 出货与送气分类
{
    [Key] [Column("系统自动编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 系统自动编号 { get; set; }
    [Column("出货分类")] [MaxLength(255)]
    public string? 出货分类 { get; set; }
    [Column("送气分类")] [MaxLength(255)]
    public string? 送气分类 { get; set; }
    [Column("显示顺序")]
    public int? 显示顺序 { get; set; }
}
