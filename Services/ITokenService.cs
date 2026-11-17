using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

/// <summary>邮箱验证 Token 签发结果：数据库实体只保存哈希，明文只用于本次邮件发送。</summary>
public sealed record VerificationTokenIssue(EmailVerificationToken Token, string PlainToken);

public interface ITokenService
{
    /// <summary>按明文 Token 查询（内部先做 SHA-256 哈希，再与库中哈希比对）。</summary>
    Task<EmailVerificationToken?> GetVerificationTokenAsync(string token);

    /// <summary>
    /// 原子消费验证 Token：查询后以“按 Id 删除且影响行数为 1”作为单次使用判定。
    /// 并发提交同一 Token 时只有一个请求能成功。
    /// </summary>
    Task<EmailVerificationToken?> ConsumeVerificationTokenAsync(string token);

    /// <summary>
    /// 生成验证 Token：数据库只保存 SHA-256 哈希，返回的 <see cref="VerificationTokenIssue.PlainToken"/>
    /// 仅用于发送邮件，不应落库/写日志。
    /// </summary>
    Task<VerificationTokenIssue> CreateVerificationTokenAsync(
        ulong userId, VerificationTokenType type, string? newEmail = null);

    Task DeleteVerificationTokenAsync(ulong tokenId);
    Task DeleteExpiredTokensAsync();
}
