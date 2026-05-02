using Microsoft.Extensions.Caching.Memory;

namespace UserManagementSystem.Services;

/// <summary>
/// JWT Token 黑名单服务，用于撤销已签发但尚未过期的 Token
/// </summary>
public interface ITokenBlacklistService
{
    /// <summary>
    /// 将 Token 的 jti 加入黑名单
    /// </summary>
    /// <param name="jti">Token 的唯一标识</param>
    /// <param name="expiresAt">Token 的过期时间，黑名单条目在此时间后自动清除</param>
    void Blacklist(string jti, DateTime expiresAt);

    /// <summary>
    /// 检查 Token 的 jti 是否在黑名单中
    /// </summary>
    bool IsBlacklisted(string jti);
}

public class TokenBlacklistService : ITokenBlacklistService
{
    private readonly IMemoryCache _cache;

    public TokenBlacklistService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public void Blacklist(string jti, DateTime expiresAt)
    {
        var remaining = expiresAt - DateTime.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            _cache.Set(GetKey(jti), true, remaining);
        }
    }

    public bool IsBlacklisted(string jti)
    {
        return _cache.TryGetValue(GetKey(jti), out _);
    }

    private static string GetKey(string jti) => $"token_blacklist_{jti}";
}
