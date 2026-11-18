using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Configuration;
using UserManagementSystem.Data;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public class TokenService : ITokenService
{
    private readonly ApplicationDbContext _context;
    private readonly AppConfiguration _appConfig;
    private readonly ILogger<TokenService> _logger;

    public TokenService(ApplicationDbContext context, AppConfiguration appConfig, ILogger<TokenService> logger)
    {
        _context = context;
        _appConfig = appConfig;
        _logger = logger;
    }

    private static string GenerateVerificationToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    /// <summary>
    /// 验证 Token 的数据库存储形式：SHA-256 十六进制（64 字符）。
    /// 数据库泄露/备份泄漏时，攻击者无法直接使用其中保存的值完成邮箱验证。
    /// </summary>
    private static string HashVerificationToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<EmailVerificationToken?> ConsumeVerificationTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var tokenHash = HashVerificationToken(token);
        var entity = await _context.EmailVerificationTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Token == tokenHash);

        if (entity is null)
        {
            return null;
        }

        // 以“删除影响行数”作为单次使用判定：并发的第二个请求会得到 0 行并视为无效。
        var affected = await _context.EmailVerificationTokens
            .Where(t => t.Id == entity.Id)
            .ExecuteDeleteAsync();

        return affected == 1 ? entity : null;
    }

    public async Task<VerificationTokenIssue> CreateVerificationTokenAsync(
        ulong userId,
        VerificationTokenType type,
        string? newEmail = null)
    {
        var oldTokens = await _context.EmailVerificationTokens
            .Where(t => t.UserId == userId && t.Type == type && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();

        _context.EmailVerificationTokens.RemoveRange(oldTokens);

        var plainToken = GenerateVerificationToken();
        var token = new EmailVerificationToken
        {
            UserId = userId,
            Token = HashVerificationToken(plainToken),
            Type = type,
            NewEmail = newEmail,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_appConfig.AppSettings.TokenExpiryMinutes),
            CreatedAt = DateTime.UtcNow
        };

        _context.EmailVerificationTokens.Add(token);
        await _context.SaveChangesAsync();

        return new VerificationTokenIssue(token, plainToken);
    }

    public async Task DeleteExpiredTokensAsync()
    {
        var deletedCount = await _context.EmailVerificationTokens
            .Where(t => t.ExpiresAt < DateTime.UtcNow)
            .ExecuteDeleteAsync();

        if (deletedCount > 0)
        {
            _logger.LogInformation("Deleted {Count} expired verification tokens", deletedCount);
        }
    }
}
