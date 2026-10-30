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
    /// <summary>会话标识：指向 <c>user_sessions.id</c>，用于会话级撤销。</summary>
    public const string SessionId = "sid";
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
        var expiresAt = DateTime.UtcNow.AddMinutes(_appConfig.Jwt.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, jti)
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
