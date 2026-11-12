using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>押金收据（对应数据库表 dbo.押金收据）</summary>
[Table("押金收据")]
public class 押金收据
{
    [Key] [Column("收据编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 收据编号 { get; set; }
    [Column("收据号码")] [MaxLength(30)]
    public string? 收据号码 { get; set; }
    [Column("收款日期")]
    public DateTime? 收款日期 { get; set; }
    [Column("客户编号")]
    public int? 客户编号 { get; set; }
    [Column("规格编号")]
    public int? 规格编号 { get; set; }
    [Column("数量")]
    public int? 数量 { get; set; }
    [Column("押金单价")]
    public decimal? 押金单价 { get; set; }
    [Column("收款金额")]
    public decimal? 收款金额 { get; set; }
    [Column("经办员工编号")]
    public int? 经办员工编号 { get; set; }
    [Column("备注")]
    public string? 备注 { get; set; }
}
