using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>过磅单（对应数据库表 dbo.过磅单）</summary>
[Table("过磅单")]
public class 过磅单
{
    [Key] [Column("过磅单编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 过磅单编号 { get; set; }
    [Column("磅单号码")] [MaxLength(30)]
    public string? 磅单号码 { get; set; }
    [Column("过磅日期")]
    public DateTime? 过磅日期 { get; set; }
    [Column("车牌号码")] [MaxLength(20)]
    public string? 车牌号码 { get; set; }
    [Column("司机员工编号")]
    public int? 司机员工编号 { get; set; }
    [Column("客户编号")]
    public int? 客户编号 { get; set; }
    [Column("总重量公斤")]
    public decimal? 总重量公斤 { get; set; }
    [Column("空车重量公斤")]
    public decimal? 空车重量公斤 { get; set; }
    [Column("净重量公斤")]
    public decimal? 净重量公斤 { get; set; }
    [Column("货物名称")] [MaxLength(50)]
    public string? 货物名称 { get; set; }
    [Column("备注")]
    public string? 备注 { get; set; }
}
