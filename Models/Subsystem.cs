using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UserManagementSystem.Models;

/// <summary>
/// 接入 UCS 的外部子系统及其访问凭证。
/// <para>
/// 凭证分两部分：<see cref="ApiKey"/> 为公开标识（可查询、可展示）；
/// <see cref="ApiSecretHash"/> 仅存 SHA256 哈希，明文只在创建/重置时返回一次。
/// </para>
/// </summary>
[Table("subsystems")]
public class Subsystem
{
    [Key]
    [Column("id")]
    public ulong Id { get; set; }

    [Required]
    [MaxLength(64)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>子系统唯一标识（英文短码，如 bwb、ihs）。</summary>
    [Required]
    [MaxLength(64)]
    [Column("code")]
    public string Code { get; set; } = string.Empty;

    [MaxLength(255)]
    [Column("description")]
    public string? Description { get; set; }

    [MaxLength(512)]
    [Column("home_url")]
    public string? HomeUrl { get; set; }

    /// <summary>停用后凭证立即失效。</summary>
    [Column("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>公开标识（明文存储，用于定位记录）。</summary>
    [Required]
    [MaxLength(64)]
    [Column("api_key")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>API Secret 的 SHA256 十六进制哈希。</summary>
    [Required]
    [MaxLength(128)]
    [Column("api_secret_hash")]
    public string ApiSecretHash { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [Column("created_by")]
    public ulong? CreatedBy { get; set; }

    /// <summary>最近一次使用凭证的时间（用于运维排查）。</summary>
    [Column("last_used_at")]
    public DateTime? LastUsedAt { get; set; }

    /// <summary>凭证过期时间；为空表示长期有效。</summary>
    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    /// <summary>该子系统声明并归属的权限码（用于区分权限来源）。</summary>
    public virtual ICollection<Permission> Permissions { get; set; } = new List<Permission>();
}
