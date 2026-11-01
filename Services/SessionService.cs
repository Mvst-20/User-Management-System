using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UserManagementSystem.Configuration;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

/// <inheritdoc cref="ISessionService"/>
public class SessionService : ISessionService
{
    /// <summary>活跃状态判定的时间窗口，与原 OnlineUserService 保持一致。</summary>
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(30);

    /// <summary>最近活跃时间的写入节流窗口，避免每个请求都 UPDATE 会话表。</summary>
    private static readonly TimeSpan TouchThrottle = TimeSpan.FromMinutes(1);

    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SessionService> _logger;
    private readonly AppConfiguration _appConfig;
    private readonly ITokenBlacklistService _blacklist;

    public SessionService(
        ApplicationDbContext context,
        IMemoryCache cache,
        ILogger<SessionService> logger,
        AppConfiguration appConfig,
        ITokenBlacklistService blacklist)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
        _appConfig = appConfig;
        _blacklist = blacklist;
    }

    public async Task<SessionIssueResult> CreateSessionAsync(User user, DeviceInfo device, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.AddDays(Math.Max(1, _appConfig.Jwt.RefreshTokenDays));
        var token = GenerateToken();
        var tokenHash = HashToken(token);

        // 同类设备只保留最近一次登录：同 device_key 的旧会话直接失效。
        // 采用「单条批量 UPDATE + 批量审计插入」，避免每个并发登录都产生多条撤销写操作；
        // 审计事件与新会话在同一次 SaveChanges 中提交。
        var replacedSessions = await _context.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == user.Id && s.DeviceKey == device.Key && s.RevokedAt == null)
            .ToListAsync(ct);

        if (replacedSessions.Count > 0)
        {
            await _context.UserSessions
                .Where(s => s.UserId == user.Id && s.DeviceKey == device.Key && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedReason, SessionRevokeReasons.ReplacedByNewDevice), ct);

            foreach (var oldSession in replacedSessions)
            {
                _cache.Remove(ActiveCacheKey(oldSession.Id));
                _context.LoginLogs.Add(new LoginLog
                {
                    UserId = user.Id,
                    EventType = LoginLogEvents.Revoked,
                    Success = true,
                    FailReason = SessionRevokeReasons.ReplacedByNewDevice,
                    Ip = oldSession.LastIp ?? oldSession.Ip,
                    Location = oldSession.Location,
                    UserAgent = oldSession.UserAgent,
                    DeviceName = oldSession.DeviceName,
                    SessionId = oldSession.Id,
                    CreatedAt = now
                });
            }

            // 不额外拉黑旧 jti：旧令牌带 sid，会话撤销后由 IsSessionActiveAsync 在
            // SessionValidationCacheSeconds（默认 5s）内拒绝；并发登录时也避免 revoked_tokens 热点写。
        }

        var session = new UserSession
        {
            UserId = user.Id,
            RefreshTokenHash = tokenHash,
            RefreshTokenExpiresAt = expiresAt,
            DeviceKey = device.Key,
            DeviceName = device.Name,
            DeviceType = device.Type,
            UserAgent = device.UserAgent,
            Ip = device.Ip,
            Location = device.Location,
            CreatedAt = now,
            LastActiveAt = now,
            LastIp = device.Ip
        };

        _context.UserSessions.Add(session);
        await _context.SaveChangesAsync(ct);

        if (replacedSessions.Count > 0)
        {
            _logger.LogInformation(
                "用户 {UserId} 的同类设备会话被新登录顶替：{Count} 个（设备 {Device}）",
                user.Id, replacedSessions.Count, device.Name);
        }

        return new SessionIssueResult(user, session, token);
    }

    public async Task<SessionIssueResult?> RotateAsync(string refreshToken, DeviceInfo device, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var tokenHash = HashToken(refreshToken);

        var session = await _context.UserSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == tokenHash, ct);

        if (session == null)
        {
            await DetectReuseAsync(tokenHash, ct);
            return null;
        }

        if (session.RevokedAt != null || session.RefreshTokenExpiresAt <= now)
        {
            return null;
        }

        var user = await _context.Users.FindAsync(new object?[] { session.UserId }, ct);
        if (user == null || user.Status != UserStatus.Normal)
        {
            await RevokeSessionInternalAsync(session, SessionRevokeReasons.Banned, ct);
            return null;
        }

        var newToken = GenerateToken();
        var newHash = HashToken(newToken);
        var newExpiresAt = now.AddDays(Math.Max(1, _appConfig.Jwt.RefreshTokenDays));

        // 原子轮换（CAS）：并发刷新同一 token 时只有一个请求能成功，
        // 影响行数为 0 说明已被其他请求轮换或会话被撤销。
        var affected = await _context.Database.ExecuteSqlRawAsync(
            "UPDATE `user_sessions` SET `refresh_token_hash` = {0}, `previous_refresh_token_hash` = {1}, " +
            "`refresh_token_expires_at` = {2}, `last_active_at` = {3}, `last_ip` = {4} " +
            "WHERE `id` = {5} AND `refresh_token_hash` = {1} AND `revoked_at` IS NULL",
            new object[] { newHash, tokenHash, newExpiresAt, now, device.Ip!, session.Id },
            ct);

        if (affected == 0)
        {
            _logger.LogWarning("Refresh Token 并发轮换冲突，已拒绝：session={SessionId} user={UserId}", session.Id, session.UserId);
            return null;
        }

        session.RefreshTokenHash = newHash;
        session.PreviousRefreshTokenHash = tokenHash;
        session.RefreshTokenExpiresAt = newExpiresAt;
        session.LastActiveAt = now;
        session.LastIp = device.Ip;

        return new SessionIssueResult(user, session, newToken);
    }

    /// <summary>
    /// 令牌不在"当前有效"集合里，检查它是否是某个会话已被轮换掉的旧令牌：
    /// 命中说明令牌可能已泄露（合法客户端不会重放旧令牌），撤销该会话。
    /// </summary>
    private async Task DetectReuseAsync(string tokenHash, CancellationToken ct)
    {
        var reused = await _context.UserSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.PreviousRefreshTokenHash == tokenHash, ct);

        if (reused == null || reused.RevokedAt != null)
        {
            return;
        }

        _logger.LogWarning(
            "检测到 Refresh Token 复用，撤销会话：session={SessionId} user={UserId}",
            reused.Id, reused.UserId);

        await RevokeSessionInternalAsync(reused, SessionRevokeReasons.RefreshReuse, ct);
    }

    public async Task<bool> IsSessionActiveAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var cacheKey = ActiveCacheKey(sessionId);
        if (_cache.TryGetValue(cacheKey, out bool cached) && cached)
        {
            return true;
        }

        var now = DateTime.UtcNow;
        var active = await _context.UserSessions
            .AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.RevokedAt == null && s.RefreshTokenExpiresAt > now, ct);

        if (active)
        {
            // 只缓存"有效"结论：撤销后由撤销方主动移除，其余实例最多滞后
            // SessionValidationCacheSeconds（默认 5 秒），与 TokenBlacklistService 的策略一致。
            var seconds = Math.Max(0, _appConfig.Security.SessionValidationCacheSeconds);
            if (seconds > 0)
            {
                _cache.Set(cacheKey, true, TimeSpan.FromSeconds(seconds));
            }
        }

        return active;
    }

    public async Task TouchAsync(string sessionId, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var throttleKey = $"session_touch:{sessionId}";
        if (_cache.TryGetValue(throttleKey, out _))
        {
            return;
        }

        _cache.Set(throttleKey, true, TouchThrottle);

        var now = DateTime.UtcNow;
        await _context.UserSessions
            .Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.LastActiveAt, now)
                .SetProperty(x => x.LastIp, ip), ct);
    }

    public async Task SetLastJtiAsync(string sessionId, string? jti, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(jti))
        {
            return;
        }

        await _context.UserSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastJti, jti), ct);
    }

    public async Task<bool> RevokeSessionAsync(ulong userId, string sessionId, string reason, CancellationToken ct = default)
    {
        var session = await _context.UserSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);

        if (session == null || session.RevokedAt != null)
        {
            return false;
        }

        await RevokeSessionInternalAsync(session, reason, ct);
        return true;
    }

    public async Task<int> RevokeAllExceptAsync(ulong userId, string? keepSessionId, string reason, CancellationToken ct = default)
    {
        var sessions = await _context.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null &&
                        (keepSessionId == null || s.Id != keepSessionId))
            .ToListAsync(ct);

        if (sessions.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        await _context.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null &&
                        (keepSessionId == null || s.Id != keepSessionId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.RevokedReason, reason), ct);

        foreach (var session in sessions)
        {
            _cache.Remove(ActiveCacheKey(session.Id));
            await BlacklistSessionTokenAsync(session);
        }

        await LogAsync(new LoginLog
        {
            UserId = userId,
            EventType = IsLogoutReason(reason) ? LoginLogEvents.Logout : LoginLogEvents.Revoked,
            Success = true,
            FailReason = reason,
            Ip = sessions[0].LastIp ?? sessions[0].Ip,
            Location = sessions[0].Location,
            DeviceName = sessions[0].DeviceName,
            SessionId = sessions[0].Id
        }, ct);

        _logger.LogInformation("用户 {UserId} 的 {Count} 个会话被撤销（原因 {Reason}）", userId, sessions.Count, reason);
        return sessions.Count;
    }

    public async Task<int> RevokeAllAsync(ulong userId, string reason, CancellationToken ct = default)
        => await RevokeAllExceptAsync(userId, null, reason, ct);

    public async Task<List<SessionResponse>> GetSessionsAsync(ulong userId, string? currentSessionId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await _context.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.RefreshTokenExpiresAt > now)
            .OrderByDescending(s => s.LastActiveAt)
            .Select(s => new SessionResponse(
                s.Id,
                s.DeviceName,
                s.DeviceType,
                s.LastIp ?? s.Ip,
                s.Location,
                s.UserAgent,
                s.CreatedAt,
                s.LastActiveAt,
                s.Id == currentSessionId,
                true))
            .ToListAsync(ct);
    }

    public async Task<int> GetOnlineUserCountAsync(CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow - OnlineWindow;

        return await _context.UserSessions
            .AsNoTracking()
            .Where(s => s.RevokedAt == null && s.LastActiveAt > threshold)
            .Select(s => s.UserId)
            .Distinct()
            .CountAsync(ct);
    }

    public async Task<LoginHistoryResponse> GetLoginHistoryAsync(
        ulong userId,
        int page,
        int pageSize,
        bool? success = null,
        string? eventType = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.LoginLogs.AsNoTracking().Where(l => l.UserId == userId);

        if (success.HasValue)
        {
            query = query.Where(l => l.Success == success.Value);
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(l => l.EventType == eventType);
        }

        // 提醒邮件的发送记录不属于「登录行为」，不进入用户可见的历史
        query = query.Where(l => l.EventType != LoginLogEvents.Alert);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LoginLogResponse(
                l.Id,
                l.EventType,
                l.Success,
                l.FailReason,
                l.Ip,
                l.Location,
                l.DeviceName,
                l.UserAgent,
                l.CreatedAt))
            .ToListAsync(ct);

        return new LoginHistoryResponse(items, total, page, pageSize);
    }

    public async Task LogAsync(LoginLog log, CancellationToken ct = default)
    {
        if (log.CreatedAt == default)
        {
            log.CreatedAt = DateTime.UtcNow;
        }

        try
        {
            _context.LoginLogs.Add(log);
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // 审计失败不能影响登录/刷新主流程
            _logger.LogWarning(ex, "写入登录审计失败：event={Event} user={UserId}", log.EventType, log.UserId);
        }
    }

    public async Task CleanupAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // 已撤销且超过 7 天的会话、或 refresh token 已过期的会话，都无需再保留
        var revokedCutoff = now.AddDays(-7);
        await _context.UserSessions
            .Where(s => (s.RevokedAt != null && s.RevokedAt < revokedCutoff) || s.RefreshTokenExpiresAt < now)
            .ExecuteDeleteAsync(ct);

        var retentionDays = Math.Max(1, _appConfig.Security.LoginHistoryRetentionDays);
        var logCutoff = now.AddDays(-retentionDays);
        await _context.LoginLogs
            .Where(l => l.CreatedAt < logCutoff)
            .ExecuteDeleteAsync(ct);
    }

    private async Task RevokeSessionInternalAsync(UserSession session, string reason, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        await _context.UserSessions
            .Where(s => s.Id == session.Id && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.RevokedReason, reason), ct);

        _cache.Remove(ActiveCacheKey(session.Id));

        // 撤销后会话校验会拒绝带 sid 的令牌；这里再补一步黑名单，
        // 让升级前签发、没有 sid 的旧 access token 也立即失效。
        await BlacklistSessionTokenAsync(session);

        await LogAsync(new LoginLog
        {
            UserId = session.UserId,
            EventType = IsLogoutReason(reason) ? LoginLogEvents.Logout : LoginLogEvents.Revoked,
            Success = true,
            FailReason = reason,
            Ip = session.LastIp ?? session.Ip,
            Location = session.Location,
            UserAgent = session.UserAgent,
            DeviceName = session.DeviceName,
            SessionId = session.Id
        }, ct);
    }

    /// <summary>
    /// 把会话最近签发的 access token（<c>last_jti</c>）加入黑名单。
    /// <para>
    /// 带 <c>sid</c> 的令牌已由会话校验拒绝；这一步额外覆盖升级前签发、没有 <c>sid</c> 的旧令牌，
    /// 避免改密、封禁、删除用户等高危操作后旧令牌一直用到自然过期。
    /// </para>
    /// </summary>
    private async Task BlacklistSessionTokenAsync(UserSession session)
    {
        if (string.IsNullOrWhiteSpace(session.LastJti))
        {
            return;
        }

        // access token 的签发时刻保存在令牌自身，这里无法精算剩余有效期，
        // 用完整有效期覆盖即可（黑名单记录会在令牌自然过期后由清理任务删除）。
        var expiresAt = DateTime.UtcNow.AddMinutes(Math.Max(1, _appConfig.Jwt.ExpiryMinutes));

        try
        {
            await _blacklist.BlacklistAsync(session.LastJti, expiresAt);
        }
        catch (Exception ex)
        {
            // 黑名单写入失败不应影响撤销结果：会话校验仍会拒绝该会话的令牌
            _logger.LogWarning(ex, "会话令牌加入黑名单失败：SessionId={SessionId}", session.Id);
        }
    }

    /// <summary>主动登出（单设备 / 全部设备）记为 logout 事件，其余撤销记为 revoked。</summary>
    private static bool IsLogoutReason(string reason)
        => reason is SessionRevokeReasons.Logout or SessionRevokeReasons.LogoutAll;

    private static string ActiveCacheKey(string sessionId) => $"session_ok:{sessionId}";

    private static string GenerateToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    /// <summary>SHA256 哈希，防止数据库泄露后 token 被直接使用。</summary>
    public static string HashToken(string token)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
