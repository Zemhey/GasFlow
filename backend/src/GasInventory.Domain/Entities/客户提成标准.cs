using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>客户提成标准（对应数据库表 dbo.客户提成标准）</summary>
[Table("客户提成标准")]
public class 客户提成标准
{
    [Key] [Column("标准编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 标准编号 { get; set; }
    [Column("客户编号")]
    public int? 客户编号 { get; set; }
    [Column("规格编号")]
    public int? 规格编号 { get; set; }
    [Column("提成基数")]
    public decimal? 提成基数 { get; set; }
    [Column("生效日期")]
    public DateTime? 生效日期 { get; set; }
    [Column("失效日期")]
    public DateTime? 失效日期 { get; set; }
}
