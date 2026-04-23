using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using UserManagementSystem.Configuration;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public class JwtService : IJwtService
{
    private readonly AppConfiguration _appConfig;

    public JwtService(AppConfiguration appConfig)
    {
        _appConfig = appConfig;
    }

    public string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_appConfig.Jwt.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("status", ((int)user.Status).ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _appConfig.Jwt.Issuer,
            audience: _appConfig.Jwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_appConfig.Jwt.ExpiryMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
