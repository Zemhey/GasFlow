using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>
/// 系统参数（对应数据库表 dbo.系统参数）。
/// v3 是"参数编号 + (参数名, 生效范围, 生效公司编号) 唯一"的结构，v2 的 参数名 单列主键已不存在。
/// 只读用它取日志策略等配置；写入走 SystemController 的 MERGE（带分组/类型等必填列）。
/// </summary>
[Table("系统参数")]
public class 系统参数
{
    [Key] [Column("参数编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 参数编号 { get; set; }
    [Column("参数名")] [MaxLength(64)]
    public string 参数名 { get; set; } = default!;
    [Column("参数值")]
    public string? 参数值 { get; set; }
    [Column("默认值")]
    public string? 默认值 { get; set; }
    [Column("值类型")] [MaxLength(10)]
    public string 值类型 { get; set; } = "string";
    [Column("取值范围")]
    public string? 取值范围 { get; set; }
    [Column("参数分组")] [MaxLength(40)]
    public string 参数分组 { get; set; } = "通用";
    [Column("参数说明")]
    public string? 参数说明 { get; set; }
    [Column("生效范围")] [MaxLength(10)]
    public string 生效范围 { get; set; } = "全局";
    [Column("是否敏感")]
    public bool 是否敏感 { get; set; }
    [Column("是否可维护")]
    public bool 是否可维护 { get; set; } = true;
}
