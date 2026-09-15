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
                var onlineUserService = scope.ServiceProvider.GetRequiredService<IOnlineUserService>();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                await tokenService.DeleteExpiredTokensAsync();

                var expiredRefreshCount = await context.Users
                    .Where(u => u.RefreshTokenExpiresAt != null && u.RefreshTokenExpiresAt < DateTime.UtcNow
                             && u.RefreshToken != null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.RefreshToken, (string?)null)
                        .SetProperty(u => u.RefreshTokenExpiresAt, (DateTime?)null),
                        cancellationToken: stoppingToken);

                if (expiredRefreshCount > 0)
                {
                    _logger.LogInformation("Cleaned {Count} expired refresh tokens", expiredRefreshCount);
                }

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

                await onlineUserService.CleanExpiredUsersAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
