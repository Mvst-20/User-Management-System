using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UserManagementSystem.Data;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public class TokenBlacklistService : ITokenBlacklistService
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;

    public TokenBlacklistService(ApplicationDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task BlacklistAsync(string jti, DateTime expiresAt)
    {
        if (expiresAt <= DateTime.UtcNow)
            return; // Token 已自然过期，无需写入黑名单

        var existing = await _context.RevokedTokens.FindAsync(jti);
        if (existing == null)
        {
            _context.RevokedTokens.Add(new RevokedToken { Jti = jti, ExpiresAt = expiresAt });
        }
        else
        {
            existing.ExpiresAt = expiresAt;
        }
        await _context.SaveChangesAsync();

        // 本实例命中缓存，避免后续重复查库
        _cache.Set(GetKey(jti), true, expiresAt - DateTime.UtcNow);
    }

    public async Task<bool> IsBlacklistedAsync(string jti)
    {
        var cacheKey = GetKey(jti);
        if (_cache.TryGetValue(cacheKey, out bool cached) && cached)
            return true;

        var revoked = await _context.RevokedTokens
            .AnyAsync(t => t.Jti == jti && t.ExpiresAt > DateTime.UtcNow);

        // 只缓存肯定结果；否定结果不缓存，保证其他实例撤销后本实例能立即感知
        if (revoked)
        {
            _cache.Set(cacheKey, true, TimeSpan.FromMinutes(5));
        }

        return revoked;
    }

    private static string GetKey(string jti) => $"token_blacklist_{jti}";
}
