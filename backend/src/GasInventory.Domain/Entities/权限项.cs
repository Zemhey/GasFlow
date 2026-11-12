using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>权限项（对应数据库表 dbo.权限项；v3 去掉了 v2 的 说明 列）</summary>
[Table("权限项")]
public class 权限项
{
    [Key] [Column("权限编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 权限编号 { get; set; }
    [Column("权限代码")] [MaxLength(20)]
    public string? 权限代码 { get; set; }
    [Column("权限名称")] [MaxLength(20)]
    public string? 权限名称 { get; set; }
    [Column("排序号")]
    public int 排序号 { get; set; }
}
