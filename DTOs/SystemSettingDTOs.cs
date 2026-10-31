namespace UserManagementSystem.DTOs;

// ============ 系统设置相关 DTOs ============

/// <summary>
/// 系统基本信息响应
/// </summary>
public record SystemInfoResponse(
    string SiteName,
    bool AllowLogin,
    bool AllowRegister
);

/// <summary>
/// 更新系统基本信息请求
/// </summary>
public record UpdateSystemInfoRequest(
    string? SiteName = null,
    bool? AllowLogin = null,
    bool? AllowRegister = null
);

/// <summary>
/// 通用设置响应
/// </summary>
public record SettingResponse(
    string Key,
    string? Value,
    string ValueType,
    string? Description,
    DateTime UpdatedAt
);

/// <summary>
/// 通用更新设置请求
/// </summary>
public record UpdateSettingRequest(
    string Value
);
