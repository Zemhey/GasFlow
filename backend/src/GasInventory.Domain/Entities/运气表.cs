using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>运气表（对应数据库表 dbo.运气表）</summary>
[Table("运气表")]
public class 运气表
{
    [Key] [Column("运气编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 运气编号 { get; set; }
    [Column("客户编号")]
    public int? 客户编号 { get; set; }
    [Column("备注")] [MaxLength(255)]
    public string? 备注 { get; set; }
    [Column("配送公里数")]
    public decimal? 配送公里数 { get; set; }
}
