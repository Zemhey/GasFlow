using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>权限矩阵缓存（对应数据库表 dbo.权限矩阵缓存）</summary>
[Table("权限矩阵缓存")]
public class 权限矩阵缓存
{
    [Key] [Column("缓存编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 缓存编号 { get; set; }
    [Column("岗位编号")]
    public int? 岗位编号 { get; set; }
    [Column("资源编号")]
    public int? 资源编号 { get; set; }
    [Column("资源名称")] [MaxLength(50)]
    public string? 资源名称 { get; set; }
    [Column("资源类型")] [MaxLength(20)]
    public string? 资源类型 { get; set; }
    [Column("排序号")]
    public int? 排序号 { get; set; }
    [Column("查看")]
    public bool 查看 { get; set; }
    [Column("新增")]
    public bool 新增 { get; set; }
    [Column("修改")]
    public bool 修改 { get; set; }
    [Column("删除")]
    public bool 删除 { get; set; }
    [Column("打印")]
    public bool 打印 { get; set; }
}
