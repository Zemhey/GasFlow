using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>
/// 员工兼岗（对应数据库表 dbo.员工兼岗）。
/// v3 用复合主键 (员工编号, 岗位编号)，取代 v2 的 员工岗位(员工岗位编号, 是否主岗位)。
/// 主岗位在 dbo.员工.主岗位编号，这里只放"兼"的那些岗位。
/// </summary>
[Table("员工兼岗")]
public class 员工兼岗
{
    [Column("员工编号")]
    public int 员工编号 { get; set; }
    [Column("岗位编号")]
    public int 岗位编号 { get; set; }
    [Column("生效日期")]
    public DateOnly? 生效日期 { get; set; }
    [Column("失效日期")]
    public DateOnly? 失效日期 { get; set; }
}
