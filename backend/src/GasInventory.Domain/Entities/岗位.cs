using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>岗位（对应数据库表 dbo.岗位；v3 增加 岗位代码/是否超级管理员/排序号）</summary>
[Table("岗位")]
public class 岗位
{
    [Key] [Column("岗位编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 岗位编号 { get; set; }
    [Column("岗位代码")] [MaxLength(20)]
    public string? 岗位代码 { get; set; }
    [Column("岗位名称")] [MaxLength(50)]
    public string? 岗位名称 { get; set; }
    [Column("岗位描述")] [MaxLength(200)]
    public string? 岗位描述 { get; set; }
    /// <summary>超级管理员岗位直接放行全部资源（替代 v2 "岗位编号 == 1" 的硬编码）。</summary>
    [Column("是否超级管理员")]
    public bool 是否超级管理员 { get; set; }
    [Column("状态")]
    public bool 状态 { get; set; }
    [Column("排序号")]
    public int 排序号 { get; set; }
}
