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

                // 清理过期邮件验证 token
                await tokenService.DeleteExpiredTokensAsync();

                // 清理过期 Refresh Token
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

                onlineUserService.CleanExpiredUsers();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
