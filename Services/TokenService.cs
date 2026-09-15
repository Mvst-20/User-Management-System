using System.Security.Cryptography;
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

    public async Task<EmailVerificationToken?> GetVerificationTokenAsync(string token)
    {
        return await _context.EmailVerificationTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token);
    }

    public async Task<EmailVerificationToken> CreateVerificationTokenAsync(
        ulong userId, 
        VerificationTokenType type, 
        string? newEmail = null)
    {
        var oldTokens = await _context.EmailVerificationTokens
            .Where(t => t.UserId == userId && t.Type == type && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
        
        _context.EmailVerificationTokens.RemoveRange(oldTokens);

        var token = new EmailVerificationToken
        {
            UserId = userId,
            Token = GenerateVerificationToken(),
            Type = type,
            NewEmail = newEmail,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_appConfig.AppSettings.TokenExpiryMinutes),
            CreatedAt = DateTime.UtcNow
        };

        _context.EmailVerificationTokens.Add(token);
        await _context.SaveChangesAsync();

        return token;
    }

    public async Task DeleteVerificationTokenAsync(ulong tokenId)
    {
        var token = await _context.EmailVerificationTokens.FindAsync(tokenId);
        if (token != null)
        {
            _context.EmailVerificationTokens.Remove(token);
            await _context.SaveChangesAsync();
        }
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
