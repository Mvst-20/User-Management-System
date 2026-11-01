using UserManagementSystem.DTOs;

namespace UserManagementSystem.Services;

public interface ISystemSettingService
{
    /// <summary>
    /// 获取系统基本信息（公开接口，不需要权限）
    /// </summary>
    Task<SystemInfoResponse> GetSystemInfoAsync();

    /// <summary>
    /// 更新系统基本信息
    /// </summary>
    Task<SystemInfoResponse> UpdateSystemInfoAsync(UpdateSystemInfoRequest request, ulong updatedBy);

    /// <summary>
    /// 获取单个设置
    /// </summary>
    Task<SettingResponse?> GetSettingAsync(string key);

    /// <summary>
    /// 更新单个设置
    /// </summary>
    Task<SettingResponse> UpdateSettingAsync(string key, UpdateSettingRequest request, ulong updatedBy);

    /// <summary>
    /// 获取所有设置
    /// </summary>
    Task<List<SettingResponse>> GetAllSettingsAsync();

    /// <summary>
    /// 幂等播种默认设置（site_name / allow_login / allow_register）。
    /// 首次启动后即可在设置列表看到默认值，而不是仅靠读取路径兜底。
    /// </summary>
    Task EnsureDefaultsAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取设置值（内部使用）
    /// </summary>
    Task<string?> GetSettingValueAsync(string key);

    /// <summary>
    /// 获取布尔类型设置值（内部使用）
    /// </summary>
    Task<bool> GetBoolSettingAsync(string key, bool defaultValue = false);
}
