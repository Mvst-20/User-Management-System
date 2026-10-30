using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UserManagementSystem.Models;

/// <summary>
/// 登录审计流水：记录登录成功/失败、令牌刷新、登出、会话被踢等安全事件，
/// 同时作为「异地/新设备」判定的历史数据来源。
/// </summary>
[Table("login_logs")]
public class LoginLog
{
    [Key]
    [Column("id")]
    public ulong Id { get; set; }

    /// <summary>关联用户；登录失败且账号不存在时为 null。</summary>
    [Column("user_id")]
    public ulong? UserId { get; set; }

    /// <summary>本次尝试使用的账号标识（用户名/邮箱），用于排查撞库。</summary>
    [Column("login_identifier")]
    [MaxLength(128)]
    public string? LoginIdentifier { get; set; }

    /// <summary>事件类型，见 <see cref="DTOs.LoginLogEvents"/>。</summary>
    [Column("event_type")]
    [MaxLength(32)]
    public string EventType { get; set; } = DTOs.LoginLogEvents.Login;

    [Column("success")]
    public bool Success { get; set; }

    /// <summary>失败原因（如「密码错误」「账号已封禁」）或撤销原因。</summary>
    [Column("fail_reason")]
    [MaxLength(128)]
    public string? FailReason { get; set; }

    [Column("ip")]
    [MaxLength(45)]
    public string? Ip { get; set; }

    [Column("location")]
    [MaxLength(128)]
    public string? Location { get; set; }

    [Column("user_agent")]
    [MaxLength(512)]
    public string? UserAgent { get; set; }

    [Column("device_name")]
    [MaxLength(128)]
    public string? DeviceName { get; set; }

    [Column("session_id")]
    [MaxLength(64)]
    public string? SessionId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
