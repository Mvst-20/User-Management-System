using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UserManagementSystem.Models;

/// <summary>
/// 登录会话：一条记录代表一台设备的登录态。
/// <para>
/// 取代原先 <c>users.refresh_token</c> 的单列设计——单列无法表达多设备，
/// 新登录会直接覆盖旧设备导致其被静默登出。
/// </para>
/// <para>
/// JWT 中的 <c>sid</c> 声明指向本表主键，因此「踢下线」= 置 <see cref="RevokedAt"/>，
/// 鉴权链路会在数秒内拒绝该会话签发的 access token。
/// </para>
/// </summary>
[Table("user_sessions")]
public class UserSession
{
    /// <summary>会话标识（32 位十六进制 GUID），同时写入 JWT 的 sid 声明。</summary>
    [Key]
    [Column("id")]
    [MaxLength(64)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Column("user_id")]
    public ulong UserId { get; set; }

    /// <summary>Refresh Token 的 SHA-256 哈希（明文只在签发时返回一次）。</summary>
    [Column("refresh_token_hash")]
    [MaxLength(128)]
    public string RefreshTokenHash { get; set; } = string.Empty;

    /// <summary>
    /// 上一枚 Refresh Token 的哈希。用于识别「已被轮换掉的令牌再次出现」——
    /// 这是令牌泄露的典型信号，命中即撤销整个会话。
    /// </summary>
    [Column("previous_refresh_token_hash")]
    [MaxLength(128)]
    public string? PreviousRefreshTokenHash { get; set; }

    [Column("refresh_token_expires_at")]
    public DateTime RefreshTokenExpiresAt { get; set; }

    /// <summary>设备归类键：浏览器 + 操作系统归一化后的哈希，同类设备只保留最近一次登录。</summary>
    [Column("device_key")]
    [MaxLength(64)]
    public string DeviceKey { get; set; } = string.Empty;

    /// <summary>可读设备名，如 "Chrome 129 · Windows 11"。</summary>
    [Column("device_name")]
    [MaxLength(128)]
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>设备类型：Desktop / Mobile / Tablet / Bot / Unknown。</summary>
    [Column("device_type")]
    [MaxLength(32)]
    public string DeviceType { get; set; } = string.Empty;

    [Column("user_agent")]
    [MaxLength(512)]
    public string? UserAgent { get; set; }

    /// <summary>首次登录 IP。</summary>
    [Column("ip")]
    [MaxLength(45)]
    public string? Ip { get; set; }

    /// <summary>首次登录归属地，如 "广东省 深圳市"。</summary>
    [Column("location")]
    [MaxLength(128)]
    public string? Location { get; set; }

    /// <summary>最近一次签发的 access token 的 jti，用于撤销时精确拉黑。</summary>
    [Column("last_jti")]
    [MaxLength(64)]
    public string? LastJti { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最近活跃时间（节流更新，1 分钟一次），也是"在线"判定依据。</summary>
    [Column("last_active_at")]
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

    [Column("last_ip")]
    [MaxLength(45)]
    public string? LastIp { get; set; }

    [Column("revoked_at")]
    public DateTime? RevokedAt { get; set; }

    /// <summary>撤销原因：logout / logout_all / revoked_by_user / replaced_by_new_device / banned / password_changed / refresh_reuse。</summary>
    [Column("revoked_reason")]
    [MaxLength(64)]
    public string? RevokedReason { get; set; }
}
