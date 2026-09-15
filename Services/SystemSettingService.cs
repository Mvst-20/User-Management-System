using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public class SystemSettingService : ISystemSettingService
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SystemSettingService> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static class SettingKeys
    {
        public const string SiteName = "site_name";
        public const string LogoUrl = "logo_url";
        public const string AllowLogin = "allow_login";
        public const string AllowRegister = "allow_register";
        public const string FriendLinks = "friend_links";
    }

    public SystemSettingService(ApplicationDbContext context, IMemoryCache cache, ILogger<SystemSettingService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<SystemInfoResponse> GetSystemInfoAsync()
    {
        var siteName = await GetSettingValueAsync(SettingKeys.SiteName) ?? "User Management System";
        var logoUrl = await GetSettingValueAsync(SettingKeys.LogoUrl);
        var allowLogin = await GetBoolSettingAsync(SettingKeys.AllowLogin, true);
        var allowRegister = await GetBoolSettingAsync(SettingKeys.AllowRegister, true);

        return new SystemInfoResponse(siteName, logoUrl, allowLogin, allowRegister);
    }

    public async Task<SystemInfoResponse> UpdateSystemInfoAsync(UpdateSystemInfoRequest request, ulong updatedBy)
    {
        if (request.SiteName != null)
            await SetSettingAsync(SettingKeys.SiteName, request.SiteName, "string", "站点名称", updatedBy);

        if (request.LogoUrl != null)
            await SetSettingAsync(SettingKeys.LogoUrl, request.LogoUrl, "string", "站点LOGO URL", updatedBy);

        if (request.AllowLogin.HasValue)
            await SetSettingAsync(SettingKeys.AllowLogin, request.AllowLogin.Value.ToString().ToLower(), "bool", "是否允许登录", updatedBy);

        if (request.AllowRegister.HasValue)
            await SetSettingAsync(SettingKeys.AllowRegister, request.AllowRegister.Value.ToString().ToLower(), "bool", "是否允许注册", updatedBy);

        return await GetSystemInfoAsync();
    }

    public async Task<FriendLinksResponse> GetFriendLinksAsync()
    {
        var json = await GetSettingValueAsync(SettingKeys.FriendLinks);
        if (string.IsNullOrEmpty(json))
        {
            return new FriendLinksResponse(new List<FriendLink>());
        }

        try
        {
            var links = JsonSerializer.Deserialize<List<FriendLink>>(json, JsonOptions);
            return new FriendLinksResponse(links ?? new List<FriendLink>());
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize friend links JSON: {Json}", json);
            return new FriendLinksResponse(new List<FriendLink>());
        }
    }

    public async Task<FriendLinksResponse> UpdateFriendLinksAsync(UpdateFriendLinksRequest request, ulong updatedBy)
    {
        var json = JsonSerializer.Serialize(request.Links, JsonOptions);
        await SetSettingAsync(SettingKeys.FriendLinks, json, "json", "友情链接", updatedBy);

        return new FriendLinksResponse(request.Links);
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
        // 保留已存在设置的 ValueType（如 bool/json），避免被覆盖为 string；仅新建时默认为 string
        var existingType = await _context.SystemSettings
            .Where(s => s.Key == key)
            .Select(s => s.ValueType)
            .FirstOrDefaultAsync();

        var setting = await SetSettingAsync(key, request.Value, existingType ?? "string", null, updatedBy);

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
}
