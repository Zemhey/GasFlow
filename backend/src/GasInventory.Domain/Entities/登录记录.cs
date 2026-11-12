using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>登录记录（对应数据库表 dbo.登录记录；v3 叫 客户端IP，并带失败原因分类）</summary>
[Table("登录记录")]
public class 登录记录
{
    [Key] [Column("登录记录编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long 登录记录编号 { get; set; }
    [Column("工号或账号")] [MaxLength(40)]
    public string? 工号或账号 { get; set; }
    [Column("员工编号")]
    public int? 员工编号 { get; set; }
    [Column("登录时间")]
    public DateTime 登录时间 { get; set; }
    [Column("登录结果")] [MaxLength(10)]
    public string? 登录结果 { get; set; }
    /// <summary>密码错误 / 账号不存在 / 账号停用 / 账号锁定 …（受 CK_登录_失败分类 约束）</summary>
    [Column("失败原因分类")] [MaxLength(20)]
    public string? 失败原因分类 { get; set; }
    [Column("失败原因")] [MaxLength(400)]
    public string? 失败原因 { get; set; }
    [Column("登录方式")] [MaxLength(20)]
    public string? 登录方式 { get; set; }
    [Column("客户端")] [MaxLength(20)]
    public string? 客户端 { get; set; }
    [Column("客户端IP")] [MaxLength(64)]
    public string? 客户端IP { get; set; }
    [Column("浏览器")] [MaxLength(80)]
    public string? 浏览器 { get; set; }
    [Column("UserAgent")] [MaxLength(400)]
    public string? UserAgent { get; set; }
}
