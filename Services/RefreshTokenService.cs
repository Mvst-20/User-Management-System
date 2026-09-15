using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Data;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

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
        var now = DateTime.UtcNow;

        // 先取出候选用户 Id（不追踪实体）
        var userId = await _context.Users
            .Where(u =>
                u.RefreshToken == tokenHash &&
                u.RefreshTokenExpiresAt > now &&
                u.Status == UserStatus.Normal)
            .Select(u => (ulong?)u.Id)
            .FirstOrDefaultAsync();

        if (userId == null)
            return null;

        // 原子轮换：仅当数据库中的 token 仍是当前值时才清除。
        // 并发刷新同一 token 时只有一个请求能成功（影响行数为 1），
        // 其余请求得到 null，避免后写覆盖先写导致的新 token 静默失效。
        var affected = await _context.Database.ExecuteSqlRawAsync(
            "UPDATE users SET refresh_token = NULL, refresh_token_expires_at = NULL " +
            "WHERE id = {0} AND refresh_token = {1}",
            userId.Value, tokenHash);

        if (affected == 0)
            return null;

        // 新的 refresh token 由调用方随后通过 GenerateAsync 生成
        return await _context.Users.FindAsync(userId.Value);
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
