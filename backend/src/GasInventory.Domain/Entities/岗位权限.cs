using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>岗位权限（对应数据库表 dbo.岗位权限；v3 是复合主键，无代理键/授权时间列）</summary>
[Table("岗位权限")]
public class 岗位权限
{
    [Column("岗位编号")]
    public int 岗位编号 { get; set; }
    [Column("资源编号")]
    public int 资源编号 { get; set; }
    [Column("权限编号")]
    public int 权限编号 { get; set; }
    [Column("备注")] [MaxLength(200)]
    public string? 备注 { get; set; }
}
