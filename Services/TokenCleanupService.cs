using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Data;

namespace UserManagementSystem.Services;

public class TokenCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TokenCleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public TokenCleanupService(IServiceProvider serviceProvider, ILogger<TokenCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Token cleanup service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
                var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                await tokenService.DeleteExpiredTokensAsync();

                // 过期会话与超期登录日志（按 Security:LoginHistoryRetentionDays 保留）
                await sessionService.CleanupAsync(stoppingToken);

                // 清理已过期的 JWT 黑名单记录（token 已自然过期，记录不再需要）
                var revokedCount = await context.RevokedTokens
                    .Where(t => t.ExpiresAt < DateTime.UtcNow)
                    .ExecuteDeleteAsync(stoppingToken);
                if (revokedCount > 0)
                {
                    _logger.LogInformation("Cleaned {Count} expired revoked-token entries", revokedCount);
                }

                var rateLimitCount = await context.RateLimitEntries
                    .Where(r => r.ExpiresAt < DateTime.UtcNow)
                    .ExecuteDeleteAsync(stoppingToken);
                if (rateLimitCount > 0)
                {
                    _logger.LogInformation("Cleaned {Count} expired rate-limit entries", rateLimitCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
