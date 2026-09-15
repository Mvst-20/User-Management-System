namespace UserManagementSystem.Services;

/// <summary>
/// JWT Token 黑名单服务，用于撤销已签发但尚未过期的 Token。
/// 数据库存储：多实例共享、服务重启后不丢失。
/// </summary>
public interface ITokenBlacklistService
{
    /// <summary>
    /// 将 Token 的 jti 加入黑名单
    /// </summary>
    /// <param name="jti">Token 的唯一标识</param>
    /// <param name="expiresAt">Token 的过期时间，黑名单条目在此时间后可安全清除</param>
    Task BlacklistAsync(string jti, DateTime expiresAt);

    /// <summary>
    /// 检查 Token 的 jti 是否在黑名单中
    /// </summary>
    Task<bool> IsBlacklistedAsync(string jti);
}
