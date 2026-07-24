using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Configuration;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
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
        try
        {
            await StartInternalAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "初始化失败，系统将以降级模式运行: {Message}", ex.Message);
            // 不重新抛出异常，允许应用在部分功能可用的情况下继续运行
        }
    }

    private async Task StartInternalAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var appConfig = scope.ServiceProvider.GetRequiredService<AppConfiguration>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _logger.LogInformation("Database: Connected successfully");

        // ============ 初始化权限和用户组 ============
        await permissionService.SeedPermissionsAndGroupsAsync();

        // ============ 创建系统初始用户（uid=0）============
        var initialGroup = await permissionService.GetGroupByNameAsync(PermissionCodes.InitialGroupName);
        var systemUser = await context.Users.FirstOrDefaultAsync(u => u.Id == 0, cancellationToken);
        if (systemUser == null)
        {
            var systemPassword = GenerateRandomPassword(appConfig.AppSettings.AdminPasswordLength);
            systemUser = new User
            {
                Id = 0,
                Username = "system",
                Email = "system@internal.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(systemPassword, 12),
                Status = UserStatus.Normal,
                EmailVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            // MySQL 默认 sql_mode 不含 NO_AUTO_VALUE_ON_ZERO，向 AUTO_INCREMENT 列插入 0
            // 会被解释为"使用下一个自增值"而非字面量 0。需要显式开启才能插入 uid=0。
            await context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    "SET @__ucs_old_mode = @@SESSION.sql_mode");
                await context.Database.ExecuteSqlRawAsync(
                    "SET SESSION sql_mode = CONCAT(@@SESSION.sql_mode, ',NO_AUTO_VALUE_ON_ZERO')");

                try
                {
                    await context.Database.ExecuteSqlRawAsync(
                        "INSERT INTO users (id, username, email, password_hash, status, email_verified_at, created_at) " +
                        "VALUES (0, {0}, {1}, {2}, {3}, {4}, {5})",
                        systemUser.Username,
                        systemUser.Email,
                        systemUser.PasswordHash,
                        (int)systemUser.Status,
                        systemUser.EmailVerifiedAt,
                        systemUser.CreatedAt
                    );
                }
                finally
                {
                    await context.Database.ExecuteSqlRawAsync(
                        "SET SESSION sql_mode = @__ucs_old_mode");
                }
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }

            if (initialGroup != null)
            {
                await permissionService.AddMemberAsync(initialGroup.Id, 0);
            }

            // ⚠️ 安全：明文密码仅输出到控制台一次，不写入文件
            _logger.LogWarning(
                "======== SYSTEM USER (uid=0) CREATED ========\n" +
                "  Username: system\n" +
                "  Email:    system@internal.local\n" +
                "  Password: {Password}\n" +
                "  Group:    {Group} (A-level supreme permission)\n" +
                "  >>> CHANGE THIS PASSWORD IMMEDIATELY <<<\n" +
                "================================================",
                systemPassword, PermissionCodes.InitialGroupName);
        }

        // ============ 确保 uid=0 始终在初始用户组 ============
        if (initialGroup != null)
        {
            var isMember = await context.UserGroupMembers
                .AnyAsync(m => m.GroupId == initialGroup.Id && m.UserId == 0, cancellationToken);
            if (!isMember)
            {
                await permissionService.AddMemberAsync(initialGroup.Id, 0);
                _logger.LogWarning("System user (uid=0) was not in the initial group — auto-repaired");
            }
        }

        // ============ 创建超级管理员账户 ============
        if (!await userService.HasAdminAsync())
        {
            var adminPassword = GenerateRandomPassword(appConfig.AppSettings.AdminPasswordLength);
            var adminUser = new User
            {
                Username = "admin",
                Email = "admin@system.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword, 12),
                Status = UserStatus.Normal,
                EmailVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            await userService.CreateUserAsync(adminUser);

            var superAdminGroup = await permissionService.GetGroupByNameAsync(PermissionCodes.SuperAdminGroupName);
            if (superAdminGroup != null)
            {
                await permissionService.AddMemberAsync(superAdminGroup.Id, adminUser.Id);
            }

            // ⚠️ 安全：明文密码仅输出到控制台一次，不写入文件
            _logger.LogWarning(
                "======== ADMIN ACCOUNT CREATED ========\n" +
                "  Username: admin\n" +
                "  Email:    admin@system.local\n" +
                "  Password: {Password}\n" +
                "  Group:    {Group} (all permissions except A)\n" +
                "  >>> CHANGE THIS PASSWORD IMMEDIATELY <<<\n" +
                "==========================================",
                adminPassword, PermissionCodes.SuperAdminGroupName);
        }
        else
        {
            _logger.LogInformation("Administrator account already exists, skipping initialization");
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

        for (int i = 0; i < length; i++)
        {
            result[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        }

        return new string(result);
    }
}
