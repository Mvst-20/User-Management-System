namespace UserManagementSystem.Auth;

/// <summary>
/// 当前 circuit 的登录会话状态（scoped）。
/// 令牌仅存于服务端内存，浏览器侧通过 <see cref="SessionPersistence"/> 加密持久化。
/// </summary>
public sealed class TokenStore
{
    /// <summary>会话变更通知（登录/登出/令牌刷新）。</summary>
    public event Action? Changed;

    /// <summary>当前会话；未登录时为 null。</summary>
    public LoginSession? Session { get; private set; }

    /// <summary>是否已完成一次会话恢复尝试（用于避免未就绪时误判为未登录）。</summary>
    public bool RestoreAttempted { get; private set; }

    public string? AccessToken => Session?.AccessToken;

    public string? RefreshToken => Session?.RefreshToken;

    public bool IsAuthenticated => Session is not null;

    public ulong? UserId => Session?.UserId;

    public string? Username => Session?.Username;

    /// <summary>标记已尝试过会话恢复（无论成功与否）。</summary>
    public void MarkRestoreAttempted() => RestoreAttempted = true;

    /// <summary>设置/替换会话并通知订阅者。</summary>
    public void SetSession(LoginSession session)
    {
        Session = session;
        RestoreAttempted = true;
        Changed?.Invoke();
    }

    /// <summary>清除会话并通知订阅者。</summary>
    public void Clear()
    {
        var had = Session is not null;
        Session = null;
        RestoreAttempted = true;
        if (had)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>访问令牌是否将在指定时间窗内过期（用于提前刷新）。</summary>
    public bool NeedsRefresh(TimeSpan threshold)
        => Session is not null && Session.ExpiresAtUtc - DateTime.UtcNow <= threshold;
}
