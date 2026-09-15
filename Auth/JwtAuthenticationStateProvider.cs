using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace UserManagementSystem.Auth;

/// <summary>
/// 基于 <see cref="TokenStore"/> 的认证状态提供者。
/// 首次求值时会尝试从浏览器侧恢复会话（受保护 sessionStorage），
/// 因此刷新页面不会丢失登录态。
/// </summary>
public sealed class JwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState AnonymousState =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly TokenStore _store;
    private readonly SessionPersistence _persistence;
    private readonly ILogger<JwtAuthenticationStateProvider> _logger;

    private Task? _restoreTask;
    private bool _restoring;

    public JwtAuthenticationStateProvider(
        TokenStore store,
        SessionPersistence persistence,
        ILogger<JwtAuthenticationStateProvider> logger)
    {
        _store = store;
        _persistence = persistence;
        _logger = logger;
        _store.Changed += OnSessionChanged;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!_store.RestoreAttempted)
        {
            _restoreTask ??= RestoreSessionAsync();
            await _restoreTask;
        }

        return BuildState();
    }

    private async Task RestoreSessionAsync()
    {
        _restoring = true;
        try
        {
            var session = await _persistence.LoadAsync();
            if (session is null)
            {
                _store.MarkRestoreAttempted();
                return;
            }

            // 访问令牌已过期且无刷新令牌时，视为无效会话
            var usable = session.ExpiresAtUtc > DateTime.UtcNow || !string.IsNullOrEmpty(session.RefreshToken);
            if (!usable)
            {
                await _persistence.ClearAsync();
                _store.MarkRestoreAttempted();
                return;
            }

            _store.SetSession(session);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "恢复登录会话失败，按未登录处理");
            _store.MarkRestoreAttempted();
        }
        finally
        {
            _restoring = false;
        }
    }

    private void OnSessionChanged()
    {
        if (_restoring)
        {
            return; // 恢复过程中由 GetAuthenticationStateAsync 直接返回结果
        }

        NotifyAuthenticationStateChanged(Task.FromResult(BuildState()));
    }

    private AuthenticationState BuildState()
    {
        var session = _store.Session;
        if (session is null)
        {
            return AnonymousState;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.UserId.ToString()),
            new(ClaimTypes.Name, session.Username)
        };

        if (!string.IsNullOrEmpty(session.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, session.Email));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: "ucs-jwt", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role);
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }
}
