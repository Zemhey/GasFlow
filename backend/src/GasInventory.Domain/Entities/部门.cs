using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>部门（对应数据库表 dbo.部门；登录后展示"财务部"这类归属）</summary>
[Table("部门")]
public class 部门
{
    [System.ComponentModel.DataAnnotations.Key]
    [Column("部门编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 部门编号 { get; set; }
    [Column("部门名称")]
    public string? 部门名称 { get; set; }
    [Column("公司编号")]
    public int? 公司编号 { get; set; }
    [Column("是否有效")]
    public bool 是否有效 { get; set; }
}
