using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>供气商（对应数据库表 dbo.供气商）</summary>
[Table("供气商")]
public class 供气商
{
    [Key] [Column("供气商编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 供气商编号 { get; set; }
    [Column("简称")] [MaxLength(50)]
    public string? 简称 { get; set; }
}
