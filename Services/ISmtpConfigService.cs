using UserManagementSystem.DTOs;

namespace UserManagementSystem.Services;

/// <summary>
/// 解密后的 SMTP 运行期参数（仅存在于内存，用于实际发信）。
/// </summary>
public sealed record SmtpRuntimeOptions(
    string Host,
    int Port,
    bool UseSsl,
    string Username,
    string Password,
    string FromAddress,
    string FromName);

/// <summary>
/// 邮件服务配置（数据库单行表 + 密码加密）。
/// </summary>
public interface ISmtpConfigService
{
    /// <summary>
    /// 获取生效的发信参数；未配置、未启用或密码无法解密时返回 null。
    /// 结果带缓存，更新配置后立即失效。
    /// </summary>
    Task<SmtpRuntimeOptions?> GetEffectiveAsync();

    /// <summary>获取管理视图（密码脱敏）。</summary>
    Task<SmtpConfigResponse> GetForAdminAsync();

    /// <summary>更新配置。传入空白密码表示保持原密码。</summary>
    Task<SmtpConfigResponse> UpdateAsync(UpdateSmtpConfigRequest request, ulong updatedBy);

    /// <summary>邮件服务是否已启用且配置完整。</summary>
    Task<bool> IsConfiguredAsync();
}
