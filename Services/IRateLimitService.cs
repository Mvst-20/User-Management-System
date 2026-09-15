namespace UserManagementSystem.Services;

/// <summary>
/// 限流服务。数据库存储计数，多实例间共享限流窗口。
/// </summary>
public interface IRateLimitService
{
    /// <summary>获取当前窗口内的计数（窗口已过期视为 0）</summary>
    Task<int> GetCountAsync(string key);

    /// <summary>递增计数；窗口不存在或已过期时从 1 开始新窗口</summary>
    Task IncrementAsync(string key, TimeSpan window);

    /// <summary>
    /// 原子地递增计数并判断是否超限。
    /// 前 maxAttempts 次返回 false，第 maxAttempts+1 次起返回 true。
    /// </summary>
    Task<bool> IsLimitedAsync(string key, int maxAttempts, TimeSpan window);

    /// <summary>清除指定 key 的计数（如登录成功后清零失败计数）</summary>
    Task ResetAsync(string key);
}
