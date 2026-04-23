using System.Security.Cryptography;
using UserManagementSystem.Configuration;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public class InitializationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InitializationService> _logger;

    public InitializationService(IServiceProvider serviceProvider, ILogger<InitializationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var consoleService = scope.ServiceProvider.GetRequiredService<IConsoleService>();
        var appConfig = scope.ServiceProvider.GetRequiredService<AppConfiguration>();
        var context = scope.ServiceProvider.GetRequiredService<Data.ApplicationDbContext>();

        // 确保数据库已创建
        await context.Database.EnsureCreatedAsync(cancellationToken);
        consoleService.PrintInfo("Database", "Connected successfully");

        // 检查并创建初始管理员账户
        if (!await userService.HasAdminAsync())
        {
            var adminPassword = GenerateRandomPassword(appConfig.AppSettings.AdminPasswordLength);
            var adminUser = new User
            {
                Username = "admin",
                Email = "admin@system.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
                Role = UserRole.Admin,
                Status = UserStatus.Normal,
                EmailVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            await userService.CreateUserAsync(adminUser);

            // 写入日志
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "logs", "init.log");
            var logContent = $@"================================================================================
  User Management System - Initial Administrator Account
  Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
================================================================================

Username: admin
Email: admin@system.local
Password: {adminPassword}

Please login and change the password immediately.
Password is masked in log file, keep it safe.

================================================================================
";

            await File.AppendAllTextAsync(logPath, logContent);

            consoleService.PrintInitSuccess("admin", "admin@system.local", adminPassword);
        }
        else
        {
            consoleService.PrintSuccess("Administrator account already exists, skipping initialization");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static string GenerateRandomPassword(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$%";
        var result = new char[length];
        var bytes = RandomNumberGenerator.GetBytes(length);

        for (int i = 0; i < length; i++)
        {
            result[i] = chars[bytes[i] % chars.Length];
        }

        return new string(result);
    }
}
