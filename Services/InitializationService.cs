using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Configuration;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Models;
using UserManagementSystem.Services;

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
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var consoleService = scope.ServiceProvider.GetRequiredService<IConsoleService>();
        var appConfig = scope.ServiceProvider.GetRequiredService<AppConfiguration>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        consoleService.PrintInfo("Database", "Connected successfully");

        // ============ 初始化权限和用户组 ============
        await permissionService.SeedPermissionsAndGroupsAsync();

        // ============ 创建系统初始用户（uid=0）============
        var initialGroup = await permissionService.GetGroupByNameAsync(PermissionCodes.InitialGroupName);
        var systemUser = await context.Users.FirstOrDefaultAsync(u => u.Id == 0);
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
                // 保存并修改 sql_mode，允许向自增列插入字面量 0
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
                    // 无论 INSERT 是否成功，都必须恢复原始 sql_mode，
                    // 避免连接归还池后影响其他操作
                    await context.Database.ExecuteSqlRawAsync(
                        "SET SESSION sql_mode = @__ucs_old_mode");
                }
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }

            // 将系统用户加入"初始用户"组
            if (initialGroup != null)
            {
                await permissionService.AddMemberAsync(initialGroup.Id, 0);
            }

            // 写入日志
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "logs", "init.log");
            var logContent = $@"================================================================================
  User Management System - System Initial User (uid=0)
  Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
================================================================================

Username: system
Email: system@internal.local
Password: {systemPassword}

This user holds the A (supreme) permission via the '{PermissionCodes.InitialGroupName}' group.
Keep these credentials extremely secure.

================================================================================
";
            await File.AppendAllTextAsync(logPath, logContent, cancellationToken);
            consoleService.PrintInitSuccess("system", "system@internal.local", systemPassword);
        }

        // ============ 确保 uid=0 始终在初始用户组 ============
        if (initialGroup != null)
        {
            var isMember = await context.UserGroupMembers
                .AnyAsync(m => m.GroupId == initialGroup.Id && m.UserId == 0);
            if (!isMember)
            {
                await permissionService.AddMemberAsync(initialGroup.Id, 0);
                _logger.LogWarning("System user (uid=0) was not in the initial group — auto-repaired");
                consoleService.PrintWarning("System user (uid=0) was moved back into the initial user group");
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

            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "logs", "init.log");
            var logContent = $@"================================================================================
  User Management System - Super Administrator Account
  Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
================================================================================

Username: admin
Email: admin@system.local
Password: {adminPassword}

This user is in the '{PermissionCodes.SuperAdminGroupName}' group with all permissions except A.
Please login and change the password immediately.

================================================================================
";
            await File.AppendAllTextAsync(logPath, logContent, cancellationToken);
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

        for (int i = 0; i < length; i++)
        {
            result[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        }

        return new string(result);
    }
}
