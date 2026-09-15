namespace UserManagementSystem.Auth;

/// <summary>
/// 管理前端的登录会话快照。
/// 仅保存最小必要信息；权限不缓存在会话中（权限变更需即时生效）。
/// </summary>
/// <param name="AccessToken">JWT 访问令牌</param>
/// <param name="RefreshToken">刷新令牌（可空）</param>
/// <param name="ExpiresAtUtc">访问令牌过期时间（UTC）</param>
/// <param name="UserId">用户 ID</param>
/// <param name="Username">用户名</param>
/// <param name="Email">邮箱</param>
/// <param name="AvatarUrl">头像地址</param>
public sealed record LoginSession(
    string AccessToken,
    string? RefreshToken,
    DateTime ExpiresAtUtc,
    ulong UserId,
    string Username,
    string? Email,
    string? AvatarUrl = null
);
