using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>打气车（对应数据库表 dbo.打气车）</summary>
[Table("打气车")]
public class 打气车
{
    [Key] [Column("打气车编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 打气车编号 { get; set; }
    [Column("车牌")] [MaxLength(20)]
    public string? 车牌 { get; set; }
    [Column("别名")] [MaxLength(20)]
    public string? 别名 { get; set; }
    [Column("司机员工编号")]
    public int? 司机员工编号 { get; set; }
    [Column("跟车员工编号")]
    public int? 跟车员工编号 { get; set; }
}
