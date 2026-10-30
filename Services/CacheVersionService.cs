using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UserManagementSystem.Configuration;
using UserManagementSystem.Data;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

/// <inheritdoc cref="ICacheVersionService"/>
public class CacheVersionService : ICacheVersionService
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheVersionService> _logger;
    private readonly TimeSpan _memoryTtl;

    public CacheVersionService(
        ApplicationDbContext context,
        IMemoryCache cache,
        ILogger<CacheVersionService> logger,
        AppConfiguration appConfig)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
        _memoryTtl = TimeSpan.FromSeconds(Math.Max(0, appConfig.Permissions.VersionCacheSeconds));
    }

    public async Task<long> GetVersionAsync(string scope, CancellationToken ct = default)
    {
        var cacheKey = MemoryKey(scope);

        // VersionCacheSeconds <= 0 表示关闭内存缓存（每次直读库，用于排障）
        if (_memoryTtl > TimeSpan.Zero && _cache.TryGetValue(cacheKey, out long cached))
        {
            return cached;
        }

        var version = await LoadAsync(scope, ct);

        if (_memoryTtl > TimeSpan.Zero)
        {
            _cache.Set(cacheKey, version, _memoryTtl);
        }

        return version;
    }

    public async Task<long> BumpAsync(string scope, CancellationToken ct = default)
    {
        long version;

        try
        {
            // 单条 UPSERT 完成「首次插入 / 后续自增」，并发安全：
            // 两个实例同时 bump 时各自得到不同版本号，不会丢更新，也不会因主键冲突失败。
            await _context.Database.ExecuteSqlRawAsync(
                "INSERT INTO `cache_versions` (`scope`, `version`, `updated_at`) VALUES ({0}, 1, UTC_TIMESTAMP()) " +
                "ON DUPLICATE KEY UPDATE `version` = `version` + 1, `updated_at` = UTC_TIMESTAMP()",
                new object[] { scope },
                ct);

            version = await LoadAsync(scope, ct);
        }
        catch (Exception ex)
        {
            // 降级：返回 0 不去中断业务写入（权限变更本身已成功落库），
            // 由缓存 TTL 兜底，同时保留告警便于发现数据库异常。
            _logger.LogWarning(ex, "递增缓存版本号失败（scope={Scope}），本次变更将依赖缓存 TTL 兜底", scope);
            _cache.Remove(MemoryKey(scope));
            return 0;
        }

        // 本实例立即生效，避免自己还要等一个内存 TTL
        _cache.Set(MemoryKey(scope), version, _memoryTtl > TimeSpan.Zero ? _memoryTtl : TimeSpan.FromSeconds(1));

        _logger.LogDebug("缓存版本号已递增：scope={Scope} version={Version}", scope, version);
        return version;
    }

    private async Task<long> LoadAsync(string scope, CancellationToken ct)
    {
        try
        {
            var version = await _context.CacheVersions
                .AsNoTracking()
                .Where(v => v.Scope == scope)
                .Select(v => (long?)v.Version)
                .FirstOrDefaultAsync(ct);

            return version ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取缓存版本号失败（scope={Scope}），降级为 0", scope);
            return 0;
        }
    }

    private static string MemoryKey(string scope) => $"cache_version:{scope}";
}
