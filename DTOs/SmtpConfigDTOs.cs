namespace UserManagementSystem.DTOs;

/// <summary>
/// 邮件服务配置（管理视图）。密码只返回"是否已设置"，不回显任何密文或明文。
/// </summary>
public record SmtpConfigResponse(
    bool Enabled,
    string Host,
    int Port,
    bool UseSsl,
    string Username,
    bool HasPassword,
    string FromAddress,
    string FromName,
    DateTime UpdatedAt,
    ulong? UpdatedBy)
{
    /// <summary>尚未配置时的默认视图。</summary>
    public static readonly SmtpConfigResponse Empty = new(
        Enabled: false,
        Host: string.Empty,
        Port: 587,
        UseSsl: true,
        Username: string.Empty,
        HasPassword: false,
        FromAddress: string.Empty,
        FromName: string.Empty,
        UpdatedAt: default,
        UpdatedBy: null);
}

/// <summary>
/// 更新邮件服务配置请求。
/// <see cref="Password"/> 为 null 或空白时表示"保持原密码不变"。
/// </summary>
public record UpdateSmtpConfigRequest(
    bool Enabled,
    string? Host = null,
    int Port = 587,
    bool UseSsl = true,
    string? Username = null,
    string? Password = null,
    string? FromAddress = null,
    string? FromName = null);

/// <summary>使用当前配置发送测试邮件。</summary>
public record SendSmtpTestRequest(
    string To
);
