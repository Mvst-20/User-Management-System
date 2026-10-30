using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

/// <summary>
/// 会话（= 一台设备的登录态）生命周期与登录审计。
/// <para>
/// 取代原 <c>IRefreshTokenService</c> 的单列 refresh token 模型：一条会话一份 refresh token，
/// 因此多设备可以并存，且每台设备都能被单独查看与撤销。
/// </para>
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// 创建会话（登录成功后调用）。会先撤销同一 <c>device_key</c> 的旧会话
    /// ——「同类设备只保留最近一次登录」。
    /// </summary>
    Task<SessionIssueResult> CreateSessionAsync(User user, DeviceInfo device, CancellationToken ct = default);

    /// <summary>
    /// 轮换 refresh token。令牌不存在、已撤销或用户不可用时返回 null；
    /// 检测到旧令牌被重复使用（疑似窃取）时撤销该会话并写安全日志。
    /// </summary>
    Task<SessionIssueResult?> RotateAsync(string refreshToken, DeviceInfo device, CancellationToken ct = default);

    /// <summary>会话是否仍有效（鉴权链路用，仅缓存"有效"结论）。</summary>
    Task<bool> IsSessionActiveAsync(string sessionId, CancellationToken ct = default);

    /// <summary>更新最近活跃时间（同会话 1 分钟内只写一次）。</summary>
    Task TouchAsync(string sessionId, string? ip, CancellationToken ct = default);

    /// <summary>记录该会话最近一次签发的 access token jti，撤销时用于精确拉黑。</summary>
    Task SetLastJtiAsync(string sessionId, string? jti, CancellationToken ct = default);

    /// <summary>撤销指定会话（用户自助踢出）。</summary>
    Task<bool> RevokeSessionAsync(ulong userId, string sessionId, string reason, CancellationToken ct = default);

    /// <summary>撤销除 <paramref name="keepSessionId"/> 外的全部会话（一键踢出其他设备）。</summary>
    Task<int> RevokeAllExceptAsync(ulong userId, string? keepSessionId, string reason, CancellationToken ct = default);

    /// <summary>撤销用户的全部会话（封禁、改密、管理员强制下线）。</summary>
    Task<int> RevokeAllAsync(ulong userId, string reason, CancellationToken ct = default);

    /// <summary>用户当前有效的登录设备列表。</summary>
    Task<List<SessionResponse>> GetSessionsAsync(ulong userId, string? currentSessionId, CancellationToken ct = default);

    /// <summary>在线用户数（30 分钟内有活跃的会话去重用户）。</summary>
    Task<int> GetOnlineUserCountAsync(CancellationToken ct = default);

    /// <summary>分页查询登录历史（可按结果、事件类型过滤）。</summary>
    Task<LoginHistoryResponse> GetLoginHistoryAsync(
        ulong userId,
        int page,
        int pageSize,
        bool? success = null,
        string? eventType = null,
        CancellationToken ct = default);

    /// <summary>写一条登录审计流水（失败不影响主流程）。</summary>
    Task LogAsync(LoginLog log, CancellationToken ct = default);

    /// <summary>清理过期会话与超期登录日志。</summary>
    Task CleanupAsync(CancellationToken ct = default);
}
