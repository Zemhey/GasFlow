using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>入仓单（对应数据库表 dbo.入仓单）</summary>
[Table("入仓单")]
public class 入仓单
{
    [Key] [Column("入仓单编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 入仓单编号 { get; set; }
    [Column("单据号码")] [MaxLength(30)]
    public string? 单据号码 { get; set; }
    [Column("入仓日期")]
    public DateTime? 入仓日期 { get; set; }
    [Column("经办员工编号")]
    public int? 经办员工编号 { get; set; }
    [Column("车牌号码")] [MaxLength(20)]
    public string? 车牌号码 { get; set; }
    [Column("备注")]
    public string? 备注 { get; set; }
}
