using System.Security.Claims;
using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Extensions;

public static class UserExtensions
{
    public static UserResponse ToResponse(this User user)
    {
        System.Text.Json.JsonElement? extras = null;
        if (!string.IsNullOrEmpty(user.Extras))
        {
            try
            {
                extras = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(user.Extras);
            }
            catch { }
        }

        return new UserResponse(
            user.Id,
            user.Username,
            user.Email,
            user.Phone,
            (int)user.Status,
            (int)user.Role,
            user.AvatarUrl,
            user.LastLoginIp,
            user.LastLoginAt,
            user.CreatedAt,
            user.EmailVerifiedAt,
            extras
        );
    }

    public static ulong? GetUserId(this ClaimsPrincipal principal)
    {
        var claim = principal.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null && ulong.TryParse(claim.Value, out var id) ? id : null;
    }

    public static UserRole? GetUserRole(this ClaimsPrincipal principal)
    {
        var claim = principal.FindFirst(ClaimTypes.Role);
        return claim != null && Enum.TryParse<UserRole>(claim.Value, out var role) ? role : null;
    }

    public static UserStatus? GetUserStatus(this ClaimsPrincipal principal)
    {
        var claim = principal.FindFirst("status");
        return claim != null && int.TryParse(claim.Value, out var status) ? (UserStatus)status : null;
    }

    public static bool IsAdmin(this ClaimsPrincipal principal)
    {
        return principal.GetUserRole() == UserRole.Admin;
    }

    public static bool IsOwnResource(this ClaimsPrincipal principal, ulong resourceUserId)
    {
        var currentUserId = principal.GetUserId();
        return currentUserId.HasValue && currentUserId.Value == resourceUserId;
    }
}
