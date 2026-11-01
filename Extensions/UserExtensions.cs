using System.Security.Claims;
using System.Text.Json;
using UserManagementSystem.DTOs;
using UserManagementSystem.Models;
using UserManagementSystem.Services;

namespace UserManagementSystem.Extensions;

public static class UserExtensions
{
    public static UserResponse ToResponse(this User user)
    {
        JsonElement? extras = null;
        if (!string.IsNullOrEmpty(user.Extras))
        {
            try
            {
                using var doc = JsonDocument.Parse(user.Extras);
                extras = doc.RootElement.Clone();
            }
            catch
            {
                // JSON解析失败时静默忽略，extras将为null
            }
        }

        return new UserResponse(
            user.Id,
            user.Username,
            user.Email,
            user.Phone,
            (int)user.Status,
            user.AvatarUrl,
            user.LastLoginIp,
            user.LastLoginAt,
            user.CreatedAt,
            user.EmailVerifiedAt,
            extras
        );
    }

    /// <summary>
    /// 读取当前用户的 ID。
    /// <para>
    /// 新令牌使用短名 <c>nameid</c>；JWT 入站映射可能把它转成 <see cref="ClaimTypes.NameIdentifier"/>，
    /// 因此两种键都尝试，兼容已签发的旧令牌。
    /// </para>
    /// </summary>
    public static ulong? GetUserId(this ClaimsPrincipal principal)
    {
        var claim = principal.FindFirst(JwtClaimNames.UserId)
                    ?? principal.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null && ulong.TryParse(claim.Value, out var id) ? id : null;
    }

    /// <summary>读取会话 ID（sid）；兼容入站映射后的 ClaimTypes.Sid。</summary>
    public static string? GetSessionId(this ClaimsPrincipal principal)
        => principal.FindFirst(JwtClaimNames.SessionId)?.Value
           ?? principal.FindFirst(ClaimTypes.Sid)?.Value;

    /// <summary>读取令牌 ID（jti），用于登出/撤销时拉黑。</summary>
    public static string? GetJti(this ClaimsPrincipal principal)
        => principal.FindFirst("jti")?.Value;

    /// <summary>
    /// 获取当前用户 ID，若未认证则抛出异常
    /// </summary>
    public static ulong GetRequiredUserId(this ClaimsPrincipal principal)
    {
        var id = principal.GetUserId();
        if (id is null)
            throw new UnauthorizedAccessException("无法获取当前用户信息");
        return id.Value;
    }

    public static bool IsOwnResource(this ClaimsPrincipal principal, ulong resourceUserId)
    {
        var currentUserId = principal.GetUserId();
        return currentUserId.HasValue && currentUserId.Value == resourceUserId;
    }

    /// <summary>
    /// 标准化分页参数
    /// </summary>
    public static (int page, int pageSize) NormalizePagination(int page, int pageSize, int maxPageSize = 100)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 || pageSize > maxPageSize ? 20 : pageSize;
        return (page, pageSize);
    }
}
