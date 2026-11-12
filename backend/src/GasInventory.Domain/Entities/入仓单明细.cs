using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>入仓单明细（对应数据库表 dbo.入仓单明细）</summary>
[Table("入仓单明细")]
public class 入仓单明细
{
    [Key] [Column("入仓明细编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 入仓明细编号 { get; set; }
    [Column("入仓单编号")]
    public int? 入仓单编号 { get; set; }
    [Column("规格编号")]
    public int? 规格编号 { get; set; }
    [Column("新瓶入仓数量")]
    public int? 新瓶入仓数量 { get; set; }
    [Column("客户瓶入仓数量")]
    public int? 客户瓶入仓数量 { get; set; }
    [Column("实瓶入仓数量")]
    public int? 实瓶入仓数量 { get; set; }
    [Column("空瓶入仓数量")]
    public int? 空瓶入仓数量 { get; set; }
    [Column("旧瓶入仓数量")]
    public int? 旧瓶入仓数量 { get; set; }
}
