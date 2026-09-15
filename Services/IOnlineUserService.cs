namespace UserManagementSystem.Services;

/// <summary>
/// 在线用户服务。数据库存储活跃记录，多实例间共享在线状态。
/// </summary>
public interface IOnlineUserService
{
    Task MarkUserOnlineAsync(string userId);
    Task MarkUserOfflineAsync(string userId);
    Task<int> GetOnlineUsersCountAsync();
    Task CleanExpiredUsersAsync(TimeSpan? timeout = null);
}
