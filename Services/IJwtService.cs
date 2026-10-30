using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

/// <summary>签发结果：调用方需要 jti 来记录到会话（撤销时可按会话精确拉黑）。</summary>
public sealed record AccessTokenResult(string Token, string Jti, DateTime ExpiresAtUtc);

public interface IJwtService
{
    /// <summary>
    /// 签发 access token。<paramref name="sessionId"/> 非空时写入 <c>sid</c> 声明，
    /// 鉴权链路据此校验会话是否已被撤销（踢下线）。
    /// </summary>
    AccessTokenResult GenerateToken(User user, string? sessionId = null);
}
