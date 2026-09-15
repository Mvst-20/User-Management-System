using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UserManagementSystem.Models;

/// <summary>
/// 限流计数条目。数据库存储，多实例间共享计数窗口。
/// </summary>
[Table("rate_limit_entries")]
public class RateLimitEntry
{
    /// <summary>限流键（如 rate_limit:login:ip:1.2.3.4）</summary>
    [Key]
    [Column("key")]
    [MaxLength(255)]
    public string Key { get; set; } = string.Empty;

    /// <summary>当前窗口内已发生的次数</summary>
    [Column("count")]
    public int Count { get; set; }

    /// <summary>窗口过期时间</summary>
    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }
}
