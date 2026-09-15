using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Data;

namespace UserManagementSystem.Services;

public class RateLimitService : IRateLimitService
{
    private readonly ApplicationDbContext _context;

    public RateLimitService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<int> GetCountAsync(string key)
    {
        var entry = await _context.RateLimitEntries.FindAsync(key);
        if (entry == null || entry.ExpiresAt <= DateTime.UtcNow)
            return 0;
        return entry.Count;
    }

    public Task IncrementAsync(string key, TimeSpan window)
        => UpsertIncrementAsync(key, window);

    public async Task<bool> IsLimitedAsync(string key, int maxAttempts, TimeSpan window)
    {
        var count = await UpsertIncrementAsync(key, window);
        return count > maxAttempts;
    }

    public async Task ResetAsync(string key)
    {
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM rate_limit_entries WHERE `key` = {0}", key);
    }

    /// <summary>
    /// 原子 upsert + 递增，返回递增后的计数值。窗口过期后自动重置为 1。
    /// </summary>
    private async Task<int> UpsertIncrementAsync(string key, TimeSpan window)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now + window;

        await _context.Database.ExecuteSqlRawAsync(@"
INSERT INTO rate_limit_entries (`key`, `count`, `expires_at`)
VALUES ({0}, 1, {1})
ON DUPLICATE KEY UPDATE
  `count` = IF(`expires_at` > {2}, `count` + 1, 1),
  `expires_at` = IF(`expires_at` > {2}, `expires_at`, {1});",
            key, expiresAt, now);

        // 用 EF 投影读取计数值（避免原生 SQL 标量查询的列别名要求）
        return await _context.RateLimitEntries
            .Where(r => r.Key == key)
            .Select(r => r.Count)
            .FirstAsync();
    }
}
