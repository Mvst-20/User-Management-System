using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace UserManagementSystem.Auth;

/// <summary>
/// 会话的浏览器侧持久化：使用受数据保护的 sessionStorage（按标签页隔离，关闭标签页即失效）。
/// 依赖 JS 互操作，因此只能在交互式渲染阶段调用。
/// </summary>
public sealed class SessionPersistence
{
    private const string StorageKey = "ucs.auth.session";

    private readonly ProtectedSessionStorage _storage;
    private readonly ILogger<SessionPersistence> _logger;

    public SessionPersistence(ProtectedSessionStorage storage, ILogger<SessionPersistence> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <summary>读取已持久化的会话；失败或不存在时返回 null。</summary>
    public async Task<LoginSession?> LoadAsync()
    {
        try
        {
            var result = await _storage.GetAsync<LoginSession>(StorageKey);
            return result.Success ? result.Value : null;
        }
        catch (Exception ex)
        {
            // 预渲染阶段或无 JS 环境时不可用，按"无会话"处理
            _logger.LogDebug(ex, "读取持久化会话失败（可能尚未进入交互式渲染）");
            return null;
        }
    }

    /// <summary>保存会话。</summary>
    public async Task SaveAsync(LoginSession session)
    {
        try
        {
            await _storage.SetAsync(StorageKey, session);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "保存会话失败，刷新页面后需要重新登录");
        }
    }

    /// <summary>清除会话。</summary>
    public async Task ClearAsync()
    {
        try
        {
            await _storage.DeleteAsync(StorageKey);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "清除持久化会话失败");
        }
    }
}
