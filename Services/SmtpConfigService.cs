using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Exceptions;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

/// <summary>
/// 邮件服务配置实现：数据库单行配置 + Data Protection 密码加密 + 内存缓存。
/// </summary>
public sealed class SmtpConfigService : ISmtpConfigService
{
    private const string CacheKey = "smtp_effective_config";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly ApplicationDbContext _context;
    private readonly IDataProtector _protector;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SmtpConfigService> _logger;

    public SmtpConfigService(
        ApplicationDbContext context,
        IDataProtectionProvider protectionProvider,
        IMemoryCache cache,
        ILogger<SmtpConfigService> logger)
    {
        _context = context;
        _protector = protectionProvider.CreateProtector("UCS.Smtp.Password.v1");
        _cache = cache;
        _logger = logger;
    }

    public async Task<SmtpRuntimeOptions?> GetEffectiveAsync()
    {
        if (_cache.TryGetValue(CacheKey, out SmtpRuntimeOptions? cached))
        {
            return cached;
        }

        var config = await _context.SmtpConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == Models.SmtpConfig.SingletonId);

        SmtpRuntimeOptions? options = null;

        if (config is { Enabled: true } &&
            !string.IsNullOrWhiteSpace(config.Host) &&
            !string.IsNullOrWhiteSpace(config.FromAddress))
        {
            options = new SmtpRuntimeOptions(
                config.Host,
                config.Port,
                config.UseSsl,
                config.Username,
                DecryptPassword(config.PasswordEncrypted) ?? string.Empty,
                config.FromAddress,
                config.FromName);
        }

        _cache.Set(CacheKey, options, CacheDuration);
        return options;
    }

    public async Task<bool> IsConfiguredAsync() => await GetEffectiveAsync() is not null;

    public async Task<SmtpConfigResponse> GetForAdminAsync()
    {
        var config = await _context.SmtpConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == Models.SmtpConfig.SingletonId);

        return config is null ? SmtpConfigResponse.Empty : ToResponse(config);
    }

    public async Task<SmtpConfigResponse> UpdateAsync(UpdateSmtpConfigRequest request, ulong updatedBy)
    {
        var host = request.Host?.Trim() ?? string.Empty;
        var fromAddress = request.FromAddress?.Trim() ?? string.Empty;

        // 启用状态下必须提供完整的连接信息，否则拒绝保存（避免"启用但不可用"的哑配置）
        if (request.Enabled)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new BusinessException(ResultCodes.ValidationError, "启用邮件服务时必须填写 SMTP 服务器地址");
            if (request.Port is <= 0 or > 65535)
                throw new BusinessException(ResultCodes.ValidationError, "SMTP 端口必须在 1-65535 之间");
            if (string.IsNullOrWhiteSpace(fromAddress))
                throw new BusinessException(ResultCodes.ValidationError, "启用邮件服务时必须填写发件人地址");
        }

        var config = await _context.SmtpConfigs
            .FirstOrDefaultAsync(c => c.Id == Models.SmtpConfig.SingletonId);

        if (config is null)
        {
            config = new Models.SmtpConfig { Id = Models.SmtpConfig.SingletonId };
            _context.SmtpConfigs.Add(config);
        }

        config.Enabled = request.Enabled;
        config.Host = host;
        config.Port = request.Port;
        config.UseSsl = request.UseSsl;
        config.Username = request.Username?.Trim() ?? string.Empty;
        config.FromAddress = fromAddress;
        config.FromName = request.FromName?.Trim() ?? string.Empty;

        // 空白密码表示保持原密码；否则加密覆盖
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            config.PasswordEncrypted = _protector.Protect(request.Password);
        }

        config.UpdatedAt = DateTime.UtcNow;
        config.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync();
        InvalidateCache();

        _logger.LogInformation("SMTP configuration updated by user {UserId} (enabled={Enabled})", updatedBy, config.Enabled);

        return ToResponse(config);
    }

    private void InvalidateCache() => _cache.Remove(CacheKey);

    private string? DecryptPassword(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(encrypted);
        }
        catch (CryptographicException ex)
        {
            // 密钥环缺失/变更导致无法解密：降级为"未配置"，提示管理员重新录入密码
            _logger.LogError(ex, "SMTP 密码解密失败（Data Protection 密钥环可能已变更或丢失），请在管理后台重新录入密码");
            return null;
        }
    }

    private static SmtpConfigResponse ToResponse(Models.SmtpConfig config) => new(
        config.Enabled,
        config.Host,
        config.Port,
        config.UseSsl,
        config.Username,
        HasPassword: !string.IsNullOrEmpty(config.PasswordEncrypted),
        config.FromAddress,
        config.FromName,
        config.UpdatedAt,
        config.UpdatedBy);
}
