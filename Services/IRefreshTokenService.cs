using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public interface IRefreshTokenService
{
    /// <summary>
    /// 为用户生成新的 Refresh Token（返回明文，数据库存哈希）
    /// </summary>
    Task<string> GenerateAsync(ulong userId);

    /// <summary>
    /// 验证 Refresh Token，成功返回用户，失败返回 null
    /// </summary>
    Task<User?> ValidateAsync(string refreshToken);

    /// <summary>
    /// 撤销用户的 Refresh Token
    /// </summary>
    Task RevokeAsync(ulong userId);
}
