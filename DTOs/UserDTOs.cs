using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace UserManagementSystem.DTOs;

// ============ 请求 DTOs ============

public record RegisterRequest(
    [Required(ErrorMessage = "用户名不能为空")]
    [StringLength(64, MinimumLength = 3, ErrorMessage = "用户名长度为3-64个字符")]
    string Username,

    [Required(ErrorMessage = "邮箱不能为空")]
    [EmailAddress(ErrorMessage = "邮箱格式不正确")]
    [StringLength(128, ErrorMessage = "邮箱长度不能超过128个字符")]
    string Email,

    [Required(ErrorMessage = "密码不能为空")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "密码长度为8-128个字符")]
    string Password,

    JsonElement? Extras = null
);

public record LoginRequest(
    [Required(ErrorMessage = "登录名不能为空")]
    string Login,

    [Required(ErrorMessage = "密码不能为空")]
    string Password
);

public record ChangePasswordRequest(
    [Required(ErrorMessage = "当前密码不能为空")]
    string CurrentPassword,

    [Required(ErrorMessage = "新密码不能为空")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "新密码长度为8-128个字符")]
    string NewPassword
);

public record ChangeEmailRequest(
    [Required(ErrorMessage = "新邮箱不能为空")]
    [EmailAddress(ErrorMessage = "邮箱格式不正确")]
    string NewEmail,

    [Required(ErrorMessage = "密码不能为空")]
    string Password
);

public record UpdateUserRequest(
    [StringLength(64, MinimumLength = 3, ErrorMessage = "用户名长度为3-64个字符")]
    string? Username = null,

    [EmailAddress(ErrorMessage = "邮箱格式不正确")]
    string? Email = null,

    [StringLength(20, ErrorMessage = "手机号长度不能超过20个字符")]
    string? Phone = null,

    int? Role = null,

    JsonElement? Extras = null
);

public record PatchUserRequest(
    [StringLength(64, MinimumLength = 3, ErrorMessage = "用户名长度为3-64个字符")]
    string? Username = null,

    [StringLength(20, ErrorMessage = "手机号长度不能超过20个字符")]
    string? Phone = null,

    JsonElement? Extras = null
);

public record ResendVerificationRequest(
    [Required(ErrorMessage = "邮箱不能为空")]
    [EmailAddress(ErrorMessage = "邮箱格式不正确")]
    string Email
);

public record SendEmailRequest(
    [Required(ErrorMessage = "收件人不能为空")]
    [EmailAddress(ErrorMessage = "邮箱格式不正确")]
    string To,

    [Required(ErrorMessage = "主题不能为空")]
    [StringLength(256, ErrorMessage = "主题长度不能超过256个字符")]
    string Subject,

    [Required(ErrorMessage = "正文不能为空")]
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
    string Email,
    bool EmailSent
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
