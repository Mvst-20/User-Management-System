using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UserManagementSystem.Models;

/// <summary>
/// 已撤销的 JWT（黑名单）。数据库存储，支持多实例共享与服务重启持久化。
/// </summary>
[Table("revoked_tokens")]
public class RevokedToken
{
    /// <summary>JWT ID（jti claim）</summary>
    [Key]
    [Column("jti")]
    [MaxLength(64)]
    public string Jti { get; set; } = string.Empty;

    /// <summary>原 Token 的过期时间；超过该时间后记录可安全清理</summary>
    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }
}
