using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Data;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public interface IRefreshTokenService
{
    /// <summary>
    /// 为用户生成新的 Refresh Token（返回明文，数据库存哈希）
    /// </summary>
    Task<string> GenerateAsync(ulong userId);

    /// <summary>
    /// 验证 Refresh Token，成功返回用户，失败返回 null
    /// </summary>
    Task<User?> ValidateAsync(string refreshToken);

    /// <summary>
    /// 撤销用户的 Refresh Token
    /// </summary>
    Task RevokeAsync(ulong userId);
}

public class RefreshTokenService : IRefreshTokenService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RefreshTokenService> _logger;

    public RefreshTokenService(ApplicationDbContext context, ILogger<RefreshTokenService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<string> GenerateAsync(ulong userId)
    {
        // 生成 64 字节随机令牌
        var tokenBytes = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToBase64String(tokenBytes);
        var tokenHash = HashToken(token);

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            throw new InvalidOperationException($"User {userId} not found");

        user.RefreshToken = tokenHash;
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(30); // 30 天有效
        await _context.SaveChangesAsync();

        return token;
    }

    public async Task<User?> ValidateAsync(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return null;

        var tokenHash = HashToken(refreshToken);

        var user = await _context.Users
            .FirstOrDefaultAsync(u =>
                u.RefreshToken == tokenHash &&
                u.RefreshTokenExpiresAt > DateTime.UtcNow &&
                u.Status == UserStatus.Normal);

        if (user == null)
            return null;

        // 验证通过后立即轮换：旧 token 失效，防止重放攻击
        // 但新的 refresh token 在调用方生成（GenerateAsync）
        return user;
    }

    public async Task RevokeAsync(ulong userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user != null)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// SHA256 哈希，防止数据库泄露后 token 被直接使用
    /// </summary>
    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }
}
