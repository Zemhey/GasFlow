using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>进气单主干（对应数据库表 dbo.进气单主干）</summary>
[Table("进气单主干")]
public class 进气单主干
{
    [Key] [Column("进气单编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 进气单编号 { get; set; }
    [Column("单据号码")] [MaxLength(30)]
    public string? 单据号码 { get; set; }
    [Column("供气商编号")]
    public int? 供气商编号 { get; set; }
    [Column("进气日期")]
    public DateTime? 进气日期 { get; set; }
    [Column("空瓶总重公斤")]
    public decimal? 空瓶总重公斤 { get; set; }
    [Column("实瓶总重公斤")]
    public decimal? 实瓶总重公斤 { get; set; }
    [Column("金额")]
    public decimal? 金额 { get; set; }
    [Column("打气车编号")]
    public int? 打气车编号 { get; set; }
    [Column("司机员工编号")]
    public int? 司机员工编号 { get; set; }
    [Column("跟车员工编号")]
    public int? 跟车员工编号 { get; set; }
    [Column("备注")]
    public string? 备注 { get; set; }
}
