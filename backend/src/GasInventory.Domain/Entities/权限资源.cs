using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>权限资源（对应数据库表 dbo.权限资源；v3 用 资源代码 + 模块 取代 v2 的 资源类型/说明）</summary>
[Table("权限资源")]
public class 权限资源
{
    [Key] [Column("资源编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 资源编号 { get; set; }
    [Column("资源代码")] [MaxLength(60)]
    public string? 资源代码 { get; set; }
    [Column("资源名称")] [MaxLength(60)]
    public string? 资源名称 { get; set; }
    [Column("模块")] [MaxLength(40)]
    public string? 模块 { get; set; }
    /// <summary>对象名：控制器与前端做资源级鉴权时使用的字符串（必须与代码里一字不差）。</summary>
    [Column("对象名")] [MaxLength(100)]
    public string? 对象名 { get; set; }
    [Column("排序号")]
    public int 排序号 { get; set; }
    [Column("是否启用")]
    public bool 是否启用 { get; set; }
}
