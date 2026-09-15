using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UserManagementSystem.Models;

/// <summary>
/// 在线用户活跃记录。数据库存储，多实例间共享在线状态。
/// </summary>
[Table("online_users")]
public class OnlineUser
{
    /// <summary>用户 ID 字符串</summary>
    [Key]
    [Column("user_id")]
    [MaxLength(64)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>最后一次活跃时间（UTC）</summary>
    [Column("last_active_at")]
    public DateTime LastActiveAt { get; set; }
}
