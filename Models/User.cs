using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UserManagementSystem.Models;

public enum UserStatus
{
    Normal = 1,      // 正常
    Banned = 2,      // 封禁
    Unverified = 3,  // 待验证
    Deleted = 4      // 软删除
}

public enum VerificationTokenType
{
    Registration = 1,   // 注册验证
    EmailChange = 2     // 修改邮箱
}

[Table("users")]
public class User
{
    [Key]
    [Column("id")]
    public ulong Id { get; set; }

    [Required]
    [MaxLength(64)]
    [Column("username")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    [Column("email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    [Column("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(20)]
    [Column("phone")]
    public string? Phone { get; set; }

    [Column("status")]
    public UserStatus Status { get; set; } = UserStatus.Unverified;

    [MaxLength(512)]
    [Column("avatar_url")]
    public string? AvatarUrl { get; set; }

    [MaxLength(45)]
    [Column("last_login_ip")]
    public string? LastLoginIp { get; set; }

    [Column("last_login_at")]
    public DateTime? LastLoginAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("email_verified_at")]
    public DateTime? EmailVerifiedAt { get; set; }

    [Column("extras", TypeName = "json")]
    public string? Extras { get; set; }

    /// <summary>
    /// 注销（软删除）时间；用于统计"注销用户"逐日趋势。恢复为非删除状态时清空。
    /// </summary>
    [Column("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<EmailVerificationToken> VerificationTokens { get; set; } = new List<EmailVerificationToken>();
}
