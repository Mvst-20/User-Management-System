using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace UserManagementSystem.Data;

/// <summary>
/// 数据库迁移执行器：启动时应用 EF Core 迁移。
/// 兼容旧版 EnsureCreated 创建的数据库（有业务表但无迁移历史时自动建立迁移基线）。
/// </summary>
public static class DatabaseMigrator
{
    public static async Task MigrateAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        var canConnect = await context.Database.CanConnectAsync();

        if (canConnect)
        {
            await EnsureBaselineAsync(context, logger);
        }

        logger.LogInformation("Database: applying migrations...");
        await context.Database.MigrateAsync();
    }

    /// <summary>
    /// 检测旧版 EnsureCreated 创建的数据库：有业务表但无 __EFMigrationsHistory 表时，
    /// 创建迁移历史表并将第一个迁移标记为已应用，避免全新迁移与现有 schema 冲突。
    /// </summary>
    private static async Task EnsureBaselineAsync(ApplicationDbContext context, ILogger logger)
    {
        var tablesExist = await TableExistsAsync(context, "users");
        var historyExists = await TableExistsAsync(context, "__EFMigrationsHistory");

        if (!tablesExist || historyExists)
            return;

        logger.LogInformation("Database: existing schema detected, baselining EF migrations...");
        var baselineMigration = context.Database.GetMigrations().First();

        await context.Database.ExecuteSqlRawAsync(@"
CREATE TABLE `__EFMigrationsHistory` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    PRIMARY KEY (`MigrationId`)
);");
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`) VALUES ({0}, {1})",
            baselineMigration, "8.0.10");
    }

    /// <summary>通过轻量探测查询判断表是否存在（不抛异常路径）。tableName 为编译期常量，无注入风险。</summary>
    private static async Task<bool> TableExistsAsync(ApplicationDbContext context, string tableName)
    {
        try
        {
            var probeSql = "SELECT 1 FROM `" + tableName + "` LIMIT 1";
            await context.Database.ExecuteSqlRawAsync(probeSql);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
