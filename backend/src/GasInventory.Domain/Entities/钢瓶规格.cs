using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>钢瓶规格（对应数据库表 dbo.钢瓶规格）</summary>
[Table("钢瓶规格")]
public class 钢瓶规格
{
    [Key] [Column("规格编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 规格编号 { get; set; }
    [Column("规格编码")] [MaxLength(20)]
    public string? 规格编码 { get; set; }
    [Column("规格名称")] [MaxLength(50)]
    public string? 规格名称 { get; set; }
    [Column("容量公斤")]
    public decimal? 容量公斤 { get; set; }
    [Column("瓶类型")] [MaxLength(20)]
    public string? 瓶类型 { get; set; }
    [Column("钢瓶单价")]
    public decimal? 钢瓶单价 { get; set; }
    [Column("押金单价")]
    public decimal? 押金单价 { get; set; }
    [Column("是否有效")]
    public bool 是否有效 { get; set; }
    [Column("气重")]
    public decimal? 气重 { get; set; }
}
