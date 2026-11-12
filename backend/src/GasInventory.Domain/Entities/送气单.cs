using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>送气单（对应数据库表 dbo.送气单）</summary>
[Table("送气单")]
public class 送气单
{
    [Key] [Column("送气单编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 送气单编号 { get; set; }
    [Column("单据号码")] [MaxLength(30)]
    public string? 单据号码 { get; set; }
    [Column("送气日期")]
    public DateTime? 送气日期 { get; set; }
    [Column("客户编号")]
    public int? 客户编号 { get; set; }
    [Column("送气员工编号")]
    public int? 送气员工编号 { get; set; }
    [Column("跟车员工编号")]
    public int? 跟车员工编号 { get; set; }
    [Column("录单员工编号")]
    public int? 录单员工编号 { get; set; }
    [Column("计提方案编号")]
    public int? 计提方案编号 { get; set; }
    [Column("付款方式编号")]
    public int? 付款方式编号 { get; set; }
    [Column("单据状态")] [MaxLength(20)]
    public string? 单据状态 { get; set; }
    [Column("备注")]
    public string? 备注 { get; set; }
    [Column("结算方式编号")]
    public int? 结算方式编号 { get; set; }
    [Column("送气分类编号")]
    public int? 送气分类编号 { get; set; }
    [Column("销量合计")]
    public decimal? 销量合计 { get; set; }
    [Column("金额合计")]
    public decimal? 金额合计 { get; set; }
}
