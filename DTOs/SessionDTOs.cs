namespace UserManagementSystem.DTOs;

/// <summary>登录审计事件类型。</summary>
public static class LoginLogEvents
{
    public const string Login = "login";
    public const string Refresh = "refresh";
    public const string Logout = "logout";

    /// <summary>会话被踢（含被新设备顶替、管理员踢出、密码变更、令牌复用）。</summary>
    public const string Revoked = "revoked";

    /// <summary>风险登录提醒已发送（同时作为提醒去重依据）。</summary>
    public const string Alert = "alert";
}

/// <summary>会话撤销原因。</summary>
public static class SessionRevokeReasons
{
    public const string Logout = "logout";
    public const string LogoutAll = "logout_all";
    public const string RevokedByUser = "revoked_by_user";
    public const string RevokedByAdmin = "revoked_by_admin";
    public const string ReplacedByNewDevice = "replaced_by_new_device";
    public const string Banned = "banned";
    public const string PasswordChanged = "password_changed";
    public const string PasswordReset = "password_reset";
    public const string UserDeleted = "user_deleted";
    public const string RefreshReuse = "refresh_reuse";
}

/// <summary>从请求头解析出的设备信息（创建会话、写审计日志共用）。</summary>
public sealed record DeviceInfo(
    string Key,
    string Name,
    string Type,
    string? UserAgent,
    string? Ip,
    string? Location);

/// <summary>已登录客户端。</summary>
public record SessionResponse(
    string Id,
    string DeviceName,
    string DeviceType,
    string? Ip,
    string? Location,
    string? UserAgent,
    DateTime CreatedAt,
    DateTime LastActiveAt,
    bool IsCurrent);

public record SessionListResponse(IReadOnlyList<SessionResponse> Items, int Total);

/// <summary>登录历史条目。</summary>
public record LoginLogResponse(
    ulong Id,
    string EventType,
    bool Success,
    string? FailReason,
    string? Ip,
    string? Location,
    string? DeviceName,
    DateTime CreatedAt);

public record LoginHistoryResponse(
    IReadOnlyList<LoginLogResponse> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>会话签发结果（登录 / 刷新成功时返回）。</summary>
public sealed record SessionIssueResult(
    UserManagementSystem.Models.User User,
    UserManagementSystem.Models.UserSession Session,
    string RefreshToken);
