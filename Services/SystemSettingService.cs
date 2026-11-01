using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Exceptions;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public class SystemSettingService : ISystemSettingService
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SystemSettingService> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>站点名称长度上限（DB 为 TEXT，这里做业务侧约束，避免写入超长内容）。</summary>
    public const int SiteNameMaxLength = 128;

    public static class SettingKeys
    {
        public const string SiteName = "site_name";
        public const string AllowLogin = "allow_login";
        public const string AllowRegister = "allow_register";
    }

    /// <summary>默认站点名称（与首次初始化播种的值一致）。</summary>
    public const string DefaultSiteName = "User Management System";

    /// <summary>
    /// 已知设置项白名单：键 → (值类型, 描述, 规范化函数)。
    /// 未知键不再静默落库，避免配置表被任意写入污染。
    /// </summary>
    private static readonly Dictionary<string, SettingDefinition> KnownSettings = new(StringComparer.Ordinal)
    {
        [SettingKeys.SiteName] = new("string", "站点名称", NormalizeSiteName),
        [SettingKeys.AllowLogin] = new("bool", "是否允许登录", NormalizeBool),
        [SettingKeys.AllowRegister] = new("bool", "是否允许注册", NormalizeBool)
    };

    private sealed record SettingDefinition(string ValueType, string Description, Func<string, string> Normalize);

    public SystemSettingService(ApplicationDbContext context, IMemoryCache cache, ILogger<SystemSettingService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<SystemInfoResponse> GetSystemInfoAsync()
    {
        var siteName = await GetSettingValueAsync(SettingKeys.SiteName) ?? DefaultSiteName;
        var allowLogin = await GetBoolSettingAsync(SettingKeys.AllowLogin, true);
        var allowRegister = await GetBoolSettingAsync(SettingKeys.AllowRegister, true);

        return new SystemInfoResponse(siteName, allowLogin, allowRegister);
    }

    public async Task<SystemInfoResponse> UpdateSystemInfoAsync(UpdateSystemInfoRequest request, ulong updatedBy)
    {
        if (request.SiteName != null)
        {
            await UpdateSettingAsync(SettingKeys.SiteName, new UpdateSettingRequest(request.SiteName), updatedBy);
        }

        if (request.AllowLogin.HasValue)
        {
            await UpdateSettingAsync(SettingKeys.AllowLogin,
                new UpdateSettingRequest(request.AllowLogin.Value ? "true" : "false"), updatedBy);
        }

        if (request.AllowRegister.HasValue)
        {
            await UpdateSettingAsync(SettingKeys.AllowRegister,
                new UpdateSettingRequest(request.AllowRegister.Value ? "true" : "false"), updatedBy);
        }

        return await GetSystemInfoAsync();
    }

    public async Task<SettingResponse?> GetSettingAsync(string key)
    {
        var setting = await _context.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == key);

        if (setting == null) return null;

        return new SettingResponse(
            setting.Key,
            setting.Value,
            setting.ValueType,
            setting.Description,
            setting.UpdatedAt
        );
    }

    public async Task<SettingResponse> UpdateSettingAsync(string key, UpdateSettingRequest request, ulong updatedBy)
    {
        var normalizedKey = (key ?? string.Empty).Trim();
        if (!KnownSettings.TryGetValue(normalizedKey, out var definition))
        {
            throw new BusinessException(ResultCodes.ValidationError, "未知的设置项");
        }

        // 严格类型校验：例如 allow_login 只接受 true/false/1/0，
        // 避免写入 "abc" 后 GetBoolSettingAsync 误判为 false 而关闭全站登录。
        var normalizedValue = definition.Normalize(request.Value ?? string.Empty);

        var setting = await SetSettingAsync(
            normalizedKey, normalizedValue, definition.ValueType, definition.Description, updatedBy);

        return new SettingResponse(
            setting.Key,
            setting.Value,
            setting.ValueType,
            setting.Description,
            setting.UpdatedAt
        );
    }

    public async Task<List<SettingResponse>> GetAllSettingsAsync()
    {
        var settings = await _context.SystemSettings
            .OrderBy(s => s.Key)
            .ToListAsync();

        return settings.Select(s => new SettingResponse(
            s.Key,
            s.Value,
            s.ValueType,
            s.Description,
            s.UpdatedAt
        )).ToList();
    }

    /// <summary>
    /// 幂等播种默认设置：全新库首次启动后即可在「系统设置」中看到三个键（O-02），
    /// 而不是仅靠读取路径的默认值兜底。
    /// </summary>
    public async Task EnsureDefaultsAsync(CancellationToken ct = default)
    {
        var existingKeys = await _context.SystemSettings
            .Select(s => s.Key)
            .ToListAsync(ct);

        var missing = new List<SystemSetting>();
        foreach (var (key, definition) in KnownSettings)
        {
            if (existingKeys.Contains(key))
            {
                continue;
            }

            missing.Add(new SystemSetting
            {
                Key = key,
                Value = key == SettingKeys.SiteName ? DefaultSiteName : "true",
                ValueType = definition.ValueType,
                Description = definition.Description,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = 0
            });
        }

        if (missing.Count == 0)
        {
            return;
        }

        _context.SystemSettings.AddRange(missing);
        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} default system settings", missing.Count);
    }

    public async Task<string?> GetSettingValueAsync(string key)
    {
        var cacheKey = $"setting_{key}";

        if (_cache.TryGetValue(cacheKey, out string? cachedValue))
            return cachedValue;

        var setting = await _context.SystemSettings
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync();

        if (setting != null)
        {
            _cache.Set(cacheKey, setting, CacheDuration);
        }

        return setting;
    }

    public async Task<bool> GetBoolSettingAsync(string key, bool defaultValue = false)
    {
        var value = await GetSettingValueAsync(key);
        if (string.IsNullOrEmpty(value))
            return defaultValue;

        return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<SystemSetting> SetSettingAsync(string key, string value, string valueType, string? description, ulong updatedBy)
    {
        var setting = await _context.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == key);

        if (setting == null)
        {
            setting = new SystemSetting
            {
                Key = key,
                Value = value,
                ValueType = valueType,
                Description = description,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = updatedBy
            };
            _context.SystemSettings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.ValueType = valueType;
            if (description != null)
                setting.Description = description;
            setting.UpdatedAt = DateTime.UtcNow;
            setting.UpdatedBy = updatedBy;
        }

        await _context.SaveChangesAsync();

        _cache.Remove($"setting_{key}");

        _logger.LogInformation("Setting '{Key}' updated by user {UserId}", key, updatedBy);

        return setting;
    }

    private static string NormalizeSiteName(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new BusinessException(ResultCodes.ValidationError, "站点名称不能为空");
        }

        if (trimmed.Length > SiteNameMaxLength)
        {
            throw new BusinessException(ResultCodes.ValidationError,
                $"站点名称不能超过 {SiteNameMaxLength} 个字符");
        }

        return trimmed;
    }

    private static string NormalizeBool(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) || trimmed == "1")
        {
            return "true";
        }

        if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase) || trimmed == "0")
        {
            return "false";
        }

        throw new BusinessException(ResultCodes.ValidationError, "该设置的值必须为 true 或 false");
    }
}
