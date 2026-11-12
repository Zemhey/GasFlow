using System.ComponentModel.DataAnnotations.Schema;

namespace GasInventory.Domain.Entities;

/// <summary>会话（对应数据库表 dbo.会话）：Web 登录后写入，支撑"在线列表 / 踢下线"</summary>
[Table("会话")]
public class 会话
{
    [System.ComponentModel.DataAnnotations.Key]
    [Column("会话编号")] [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int 会话编号 { get; set; }
    [Column("会话ID")]
    public string? 会话ID { get; set; }
    [Column("员工编号")]
    public int 员工编号 { get; set; }
    [Column("登录时间")]
    public DateTime 登录时间 { get; set; }
    [Column("最后活动时间")]
    public DateTime 最后活动时间 { get; set; }
    [Column("过期时间")]
    public DateTime? 过期时间 { get; set; }
    [Column("客户端IP")]
    public string? 客户端IP { get; set; }
    [Column("客户端")]
    public string 客户端 { get; set; } = "Web";
    [Column("状态")]
    public string 状态 { get; set; } = "在线";
}
