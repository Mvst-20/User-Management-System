using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UserManagementSystem.Models;

[Table("email_verification_tokens")]
public class EmailVerificationToken
{
    [Key]
    [Column("id")]
    public ulong Id { get; set; }

    [Column("user_id")]
    public ulong UserId { get; set; }

    /// <summary>
    /// 验证 Token 的 SHA-256 十六进制哈希（不是明文）。
    /// 明文仅在创建时返回给邮件发送流程，数据库泄露时无法直接复用。
    /// </summary>
    [Required]
    [MaxLength(255)]
    [Column("token")]
    public string Token { get; set; } = string.Empty;

    [Column("type")]
    public VerificationTokenType Type { get; set; }

    [MaxLength(128)]
    [Column("new_email")]
    public string? NewEmail { get; set; }

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("UserId")]
    public virtual User User { get; set; } = null!;
}
