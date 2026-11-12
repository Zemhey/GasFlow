using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>客户（对应数据库表 dbo.客户）</summary>
[Table("客户")]
public class 客户
{
    [Key] [Column("客户编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 客户编号 { get; set; }
    [Column("客户编码")] [MaxLength(20)]
    public string? 客户编码 { get; set; }
    [Column("客户简称")] [MaxLength(50)]
    public string? 客户简称 { get; set; }
    [Column("客户全称")] [MaxLength(100)]
    public string? 客户全称 { get; set; }
    [Column("联系人")] [MaxLength(50)]
    public string? 联系人 { get; set; }
    [Column("联系电话")] [MaxLength(20)]
    public string? 联系电话 { get; set; }
    [Column("客户地址")] [MaxLength(255)]
    public string? 客户地址 { get; set; }
    [Column("业务员编号")]
    public int? 业务员编号 { get; set; }
    [Column("客户分类编号")]
    public int? 客户分类编号 { get; set; }
    [Column("客户区域编号")]
    public int? 客户区域编号 { get; set; }
    [Column("客户状态编号")]
    public int? 客户状态编号 { get; set; }
    [Column("计提方案编号")]
    public int? 计提方案编号 { get; set; }
    [Column("结算方式编号")]
    public int? 结算方式编号 { get; set; }
    [Column("付款方式编号")]
    public int? 付款方式编号 { get; set; }
    [Column("结算账期编号")]
    public int? 结算账期编号 { get; set; }
    [Column("结算备注")] [MaxLength(255)]
    public string? 结算备注 { get; set; }
    [Column("是否有效")]
    public bool 是否有效 { get; set; }
}
