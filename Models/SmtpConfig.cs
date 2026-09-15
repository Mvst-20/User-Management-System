using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UserManagementSystem.Models;

/// <summary>
/// 邮件服务配置（单行表，主键固定为 1）。
/// 密码以 Data Protection 加密后存储于 <see cref="PasswordEncrypted"/>，永不落明文。
/// </summary>
[Table("smtp_config")]
public class SmtpConfig
{
    /// <summary>单行配置固定主键。</summary>
    public const ulong SingletonId = 1;

    [Key]
    [Column("id")]
    public ulong Id { get; set; } = SingletonId;

    /// <summary>是否启用邮件服务（停用时所有发信请求返回明确的业务错误）。</summary>
    [Column("enabled")]
    public bool Enabled { get; set; }

    [MaxLength(255)]
    [Column("host")]
    public string Host { get; set; } = string.Empty;

    [Column("port")]
    public int Port { get; set; } = 587;

    /// <summary>非标准端口（非 465/587）时决定使用 SSL 直连还是 STARTTLS。</summary>
    [Column("use_ssl")]
    public bool UseSsl { get; set; } = true;

    [MaxLength(255)]
    [Column("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>加密后的 SMTP 密码（Data Protection）。</summary>
    [MaxLength(1024)]
    [Column("password_encrypted")]
    public string? PasswordEncrypted { get; set; }

    [MaxLength(255)]
    [Column("from_address")]
    public string FromAddress { get; set; } = string.Empty;

    [MaxLength(128)]
    [Column("from_name")]
    public string FromName { get; set; } = string.Empty;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [Column("updated_by")]
    public ulong? UpdatedBy { get; set; }
}
