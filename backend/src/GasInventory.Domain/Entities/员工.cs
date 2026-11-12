using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>
/// 员工（对应数据库表 dbo.员工）。
/// v3 列名口径（后端升 v3 时对齐，勿再改回 v2 的 密码/岗位）：
///   岗位       → 主岗位编号
///   密码       → 密码哈希 / 密码盐 / 密码迭代次数（PBKDF2-SHA256）
///   初始密码是否已改 / 失败登录次数 / 锁定至 参与登录流程。
/// </summary>
[Table("员工")]
public class 员工
{
    [Key] [Column("员工编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 员工编号 { get; set; }
    [Column("工号")] [MaxLength(20)]
    public string? 工号 { get; set; }
    [Column("姓名")] [MaxLength(50)]
    public string? 姓名 { get; set; }

    [Column("所属公司编号")]
    public int? 所属公司编号 { get; set; }
    [Column("所属网点编号")]
    public int? 所属网点编号 { get; set; }
    [Column("部门编号")]
    public int? 部门编号 { get; set; }
    /// <summary>主岗位。v3 叫 主岗位编号（v2 叫 岗位）。</summary>
    [Column("主岗位编号")]
    public int? 主岗位编号 { get; set; }

    [Column("联系电话")] [MaxLength(20)]
    public string? 联系电话 { get; set; }
    [Column("是否在职")]
    public bool 是否在职 { get; set; }
    [Column("是否司机")]
    public bool 是否司机 { get; set; }
    [Column("是否送瓶员")]
    public bool 是否送瓶员 { get; set; }

    /// <summary>PBKDF2-SHA256 派生值（32 字节）。为 NULL 表示"尚未开通登录"。</summary>
    [Column("密码哈希")]
    public byte[]? 密码哈希 { get; set; }
    /// <summary>每个员工独立的 16 字节盐。</summary>
    [Column("密码盐")]
    public byte[]? 密码盐 { get; set; }
    [Column("密码迭代次数")]
    public int? 密码迭代次数 { get; set; }
    /// <summary>false = 还在用初始密码，前端据此强制跳转改密页。</summary>
    [Column("初始密码是否已改")]
    public bool 初始密码是否已改 { get; set; }
    [Column("最后登录时间")]
    public DateTime? 最后登录时间 { get; set; }
    [Column("失败登录次数")]
    public int 失败登录次数 { get; set; }
    [Column("锁定至")]
    public DateTime? 锁定至 { get; set; }
}
