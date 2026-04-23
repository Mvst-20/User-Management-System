using System.Text.Json;

namespace UserManagementSystem.DTOs;

// ============ 请求 DTOs ============

public record RegisterRequest(
    string Username,
    string Email,
    string Password,
    JsonElement? Extras = null
);

public record LoginRequest(
    string Login,       // 可以是 username 或 email
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
    int? Role = null,
    JsonElement? Extras = null
);

public record PatchUserRequest(
    string? Username = null,
    string? Phone = null,
    JsonElement? Extras = null
);

public record ResendVerificationRequest(
    string Email
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
    int Role,
    string? AvatarUrl,
    string? LastLoginIp,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    DateTime? EmailVerifiedAt,
    JsonElement? Extras
);

public record LoginDataResponse(
    string Token,
    string TokenType,
    int ExpiresIn,
    UserResponse User
);

public record RegisterDataResponse(
    string Email
);

public record AvatarDataResponse(
    string AvatarUrl
);

public record ServerStatusResponse(
    string Uptime,
    double CpuUsage,
    double MemoryUsage,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    DateTime ServerTime,
    string DatabaseStatus,
    string Version
);
