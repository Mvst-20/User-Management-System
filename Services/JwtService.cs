using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using UserManagementSystem.Configuration;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

/// <summary>JWT 中的自定义声明名。</summary>
public static class JwtClaimNames
{
    /// <summary>用户 ID（短名，接入方契约）。</summary>
    public const string UserId = "nameid";

    /// <summary>用户名（短名，接入方契约）。</summary>
    public const string UniqueName = "unique_name";

    /// <summary>邮箱（短名，接入方契约）。</summary>
    public const string Email = "email";

    /// <summary>会话标识：指向 <c>user_sessions.id</c>，用于会话级撤销。</summary>
    public const string SessionId = "sid";

    /// <summary>签发时间（epoch 秒）。</summary>
    public const string IssuedAt = "iat";
}

public class JwtService : IJwtService
{
    private readonly AppConfiguration _appConfig;

    public JwtService(AppConfiguration appConfig)
    {
        _appConfig = appConfig;
    }

    public AccessTokenResult GenerateToken(User user, string? sessionId = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_appConfig.Jwt.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jti = Guid.NewGuid().ToString();
        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_appConfig.Jwt.ExpiryMinutes);

        // 声明名遵循接入指南的短名契约（nameid/unique_name/email），
        // 不再使用 ClaimTypes 的完整 URI；iat 供接入方计算 exp-iat 有效期。
        var claims = new List<Claim>
        {
            new(JwtClaimNames.UserId, user.Id.ToString()),
            new(JwtClaimNames.UniqueName, user.Username),
            new(JwtClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtClaimNames.IssuedAt,
                new DateTimeOffset(issuedAt).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            claims.Add(new Claim(JwtClaimNames.SessionId, sessionId));
        }

        var token = new JwtSecurityToken(
            issuer: _appConfig.Jwt.Issuer,
            audience: _appConfig.Jwt.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        return new AccessTokenResult(new JwtSecurityTokenHandler().WriteToken(token), jti, expiresAt);
    }
}
