using System.Text.Json;

namespace UserManagementSystem.DTOs;

// ============ 请求 DTOs ============
// 注意：Minimal API (.NET 8) 默认不执行 DataAnnotations 验证，请求校验在端点/业务层进行

public record RegisterRequest(
    string Username,
    string Email,
    string Password,
    JsonElement? Extras = null
);

public record LoginRequest(
    string Login,
    string Password
);

public record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword
);

public record ChangeEmailRequest(
    string NewEmail,
    string Password
);

public record UpdateUserRequest(
    string? Username = null,
    string? Email = null,
    string? Phone = null,
    JsonElement? Extras = null,

    /// <summary>用户状态：1=正常、2=封禁、3=未验证。仅管理员可修改（不允许改自己）。</summary>
    int? Status = null
);

public record PatchUserRequest(
    string? Username = null,
    string? Phone = null,
    JsonElement? Extras = null,

    /// <summary>用户状态：1=正常、2=封禁、3=未验证。仅管理员可修改（不允许改自己）。</summary>
    int? Status = null
);

public record ResendVerificationRequest(
    string Email
);

/// <summary>
/// 管理员创建用户请求。密码留空时由服务端生成并一次性返回。
/// </summary>
public record AdminCreateUserRequest(
    string Username,
    string Email,
    string? Password = null,
    string? Phone = null,

    /// <summary>是否需要完成邮箱验证后才能登录；默认 false（创建后即可登录）。</summary>
    bool RequireEmailVerification = false,

    /// <summary>初始权限组；受操作者权限级别约束（只能分配级别更低的组）。</summary>
    List<ulong>? GroupIds = null
);

/// <summary>管理员创建用户结果；仅在服务端生成密码时返回明文。</summary>
public record AdminCreateUserResponse(
    ulong Id,
    string Username,
    string Email,
    string? GeneratedPassword
);

public record RefreshTokenRequest(
    string RefreshToken
);

public record SendEmailRequest(
    string To,
    string Subject,
    string Body
);

// ============ 响应 DTOs ============

public record UserResponse(
    ulong Id,
    string Username,
    string Email,
    string? Phone,
    int Status,
    string? AvatarUrl,
    string? LastLoginIp,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    DateTime? EmailVerifiedAt,
    JsonElement? Extras,

    /// <summary>
    /// 用户的最高权限级别（所属权限组中数值最小的 level，<strong>数值越小权限越高</strong>）。
    /// 前端据此判断能否操作该用户；真正的校验仍在服务端。
    /// </summary>
    int PrivilegeLevel = PermissionCodes.GroupLevel.NoGroup
);

public record LoginDataResponse(
    string Token,
    string TokenType,
    int ExpiresIn,
    UserResponse User,
    string? RefreshToken = null,
    int RefreshTokenExpiresIn = 2592000
);

public record RegisterDataResponse(
    string Email,
    bool EmailSent
);

public record AvatarUploadDataResponse(
    string AvatarUrl,
    bool HasThumbnail
);

public record ServerStatusResponse(
    string Uptime,
    double CpuUsage,
    double MemoryUsage,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    long NetworkBytesSent,
    long NetworkBytesReceived,
    double NetworkSendRate,
    double NetworkReceiveRate,
    DateTime ServerTime,
    string DatabaseStatus,
    string Version
);

// ============ 管理前端支撑 DTOs ============

/// <summary>
/// 当前登录用户的"有效权限码"集合（已展开 A/B/C 特殊权限的覆盖效果）。
/// 供管理前端控制菜单与按钮显隐；实际鉴权仍由端点权限过滤器执行。
/// </summary>
public record MyPermissionsResponse(
    ulong UserId,
    List<string> Codes,

    /// <summary>
    /// 当前用户的最高权限级别（所属权限组中数值最小的 level，<strong>数值越小权限越高</strong>）。
    /// 前端据此判断"能否管理某个用户/权限组"；真正的校验仍在服务端。
    /// </summary>
    int Level = PermissionCodes.GroupLevel.NoGroup,

    /// <summary>
    /// 拉取这份快照时的全局权限版本号。前端轮询
    /// <c>/api/users/me/permissions/version</c>，发现版本变化即重新拉取。
    /// </summary>
    long Version = 0
);

/// <summary>
/// 管理员重置指定用户密码请求。
/// <see cref="NewPassword"/> 为空时由服务端生成随机密码并一次性返回。
/// </summary>
public record ResetPasswordRequest(
    string? NewPassword = null
);

/// <summary>
/// 管理员重置密码结果。仅在服务端生成密码时返回明文（此后再无法获取）。
/// </summary>
public record ResetPasswordResponse(
    ulong UserId,
    string Username,
    string? GeneratedPassword
);
