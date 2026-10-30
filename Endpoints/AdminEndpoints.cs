using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Configuration;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Extensions;
using UserManagementSystem.Logging;
using UserManagementSystem.Models;
using UserManagementSystem.Services;
using UserManagementSystem.Services.Api;

namespace UserManagementSystem.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        // 在线用户数量（C 权限覆盖，或 admin:stats 权限）
        app.MapGet("/api/admin/stats/online-users", async (
            [FromServices] IUserService userService) =>
        {
            var count = await userService.GetOnlineUsersCountAsync();
            return Results.Ok(new ApiResponse(ResultCodes.AdminStatsSuccess, "获取成功", new { OnlineUsers = count }));
        }).RequireAuthorization().RequirePermission(PermissionCodes.AdminStats);

        // 总用户数量（排除已删除用户）
        app.MapGet("/api/admin/stats/total-users", async (
            [FromServices] IUserService userService) =>
        {
            var count = await userService.GetTotalUsersCountAsync();
            return Results.Ok(new ApiResponse(ResultCodes.AdminStatsSuccess, "获取成功", new { TotalUsers = count }));
        }).RequireAuthorization().RequirePermission(PermissionCodes.AdminStats);

        // 新注册用户数量
        app.MapGet("/api/admin/stats/new-users", async (
            [FromQuery] int days,
            [FromServices] IUserService userService) =>
        {
            if (days <= 0)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "days 参数必须为正整数"));
            }

            var count = await userService.GetNewUsersCountAsync(days);
            return Results.Ok(new ApiResponse(ResultCodes.AdminStatsSuccess, "获取成功", new
            {
                NewUsers = count,
                Days = days,
                StartDate = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd"),
                EndDate = DateTime.UtcNow.ToString("yyyy-MM-dd")
            }));
        }).RequireAuthorization().RequirePermission(PermissionCodes.AdminStats);

        // 活跃用户数量
        app.MapGet("/api/admin/stats/active-users", async (
            [FromQuery] int days,
            [FromServices] IUserService userService) =>
        {
            if (days <= 0)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "days 参数必须为正整数"));
            }

            var count = await userService.GetActiveUsersCountAsync(days);
            return Results.Ok(new ApiResponse(ResultCodes.AdminStatsSuccess, "获取成功", new
            {
                ActiveUsers = count,
                Days = days,
                StartDate = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd"),
                EndDate = DateTime.UtcNow.ToString("yyyy-MM-dd")
            }));
        }).RequireAuthorization().RequirePermission(PermissionCodes.AdminStats);

        // 用户统计逐日趋势（仪表盘折线图数据源：总用户/新增/活跃，另附当前在线）
        app.MapGet("/api/admin/stats/new-users/daily", async (
            [FromQuery] int days,
            [FromServices] ApplicationDbContext context,
            [FromServices] IUserService userService) =>
        {
            if (days is <= 0 or > 365)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "days 参数必须为 1~365 的正整数"));
            }

            // created_at / last_login_at 均以 UTC 存储：直接按 UTC 分日会把本地凌晨前的事件计入前一天，
            // 因此按本地时区偏移换算后再分日（CONVERT_TZ 用固定偏移，无需加载时区表）
            var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
            var sign = offset >= TimeSpan.Zero ? "+" : "-";
            var offsetText = $"{sign}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}";

            var startLocal = DateTime.Today.AddDays(-(days - 1));
            var startUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime();

            var rows = await context.Database
                .SqlQuery<DailyCountRow>($@"
                    SELECT DATE(CONVERT_TZ(created_at, '+00:00', {offsetText})) AS `Date`, COUNT(*) AS `Count`
                    FROM users
                    WHERE status <> 4 AND created_at >= {startUtc}
                    GROUP BY `Date`")
                .ToListAsync();
            var counts = rows.ToDictionary(row => row.Date.Date, row => row.Count);

            // 活跃口径：last_login_at 落在当天的用户数（与"近 N 天活跃"统计同源，按日近似）
            var activeRows = await context.Database
                .SqlQuery<DailyCountRow>($@"
                    SELECT DATE(CONVERT_TZ(last_login_at, '+00:00', {offsetText})) AS `Date`, COUNT(*) AS `Count`
                    FROM users
                    WHERE status <> 4 AND last_login_at IS NOT NULL AND last_login_at >= {startUtc}
                    GROUP BY `Date`")
                .ToListAsync();
            var activeCounts = activeRows.ToDictionary(row => row.Date.Date, row => row.Count);

            // 注销口径：deleted_at 落在当天的用户数（列为此前未记录，历史注销无时间戳故不计入）
            var deletedRows = await context.Database
                .SqlQuery<DailyCountRow>($@"
                    SELECT DATE(CONVERT_TZ(deleted_at, '+00:00', {offsetText})) AS `Date`, COUNT(*) AS `Count`
                    FROM users
                    WHERE status = 4 AND deleted_at IS NOT NULL AND deleted_at >= {startUtc}
                    GROUP BY `Date`")
                .ToListAsync();
            var deletedCounts = deletedRows.ToDictionary(row => row.Date.Date, row => row.Count);

            // 总用户：范围起点前的存量用户 + 逐日新增累计
            var baseCount = await context.Users
                .CountAsync(u => u.Status != UserStatus.Deleted && u.CreatedAt < startUtc);

            var cumulative = 0;
            var items = Enumerable.Range(0, days)
                .Select(i =>
                {
                    var date = startLocal.AddDays(i);
                    var newUsers = counts.GetValueOrDefault(date, 0);
                    cumulative += newUsers;
                    return new NewUsersDailyItem(
                        date.ToString("yyyy-MM-dd"),
                        newUsers,
                        baseCount + cumulative,
                        activeCounts.GetValueOrDefault(date, 0),
                        deletedCounts.GetValueOrDefault(date, 0));
                })
                .ToList();

            var onlineUsers = await userService.GetOnlineUsersCountAsync();

            return Results.Ok(new ApiResponse(ResultCodes.AdminStatsSuccess, "获取成功", new
            {
                Days = days,
                StartDate = startLocal.ToString("yyyy-MM-dd"),
                EndDate = DateTime.Today.ToString("yyyy-MM-dd"),
                OnlineUsers = onlineUsers,
                Items = items
            }));
        }).RequireAuthorization().RequirePermission(PermissionCodes.AdminStats);

        // 服务器状态（C 权限覆盖，或 admin:server 权限）
        app.MapGet("/api/admin/server/status", async (
            [FromServices] IServiceProvider serviceProvider,
            [FromServices] ISystemMonitorService monitorService) =>
        {
            // 获取系统级监控数据
            var status = monitorService.GetSystemStatus();

            string dbStatus;
            try
            {
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.ExecuteSqlRawAsync("SELECT 1");
                dbStatus = "Connected";
            }
            catch (Exception ex)
            {
                dbStatus = $"Error: {ex.Message}";
            }

            var response = new ServerStatusResponse(
                $"{status.Uptime.Days}d {status.Uptime.Hours}h {status.Uptime.Minutes}m {status.Uptime.Seconds}s",
                status.CpuUsage,
                status.MemoryUsage,
                status.MemoryUsedBytes,
                status.MemoryTotalBytes,
                status.NetworkBytesSent,
                status.NetworkBytesReceived,
                status.NetworkSendRate,
                status.NetworkReceiveRate,
                DateTime.UtcNow,
                dbStatus,
                AppInfo.Version
                );

            return Results.Ok(new ApiResponse(ResultCodes.ServerStatusSuccess, "获取成功", response));
        }).RequireAuthorization().RequirePermission(PermissionCodes.AdminServer);

        // 发送测试邮件
        app.MapPost("/api/admin/email/test", async (
            [FromBody] SendEmailRequest request,
            [FromServices] IEmailService emailService,
            [FromServices] ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(request.To))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "收件人邮箱不能为空"));
            }

            if (string.IsNullOrWhiteSpace(request.Subject))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "邮件主题不能为空"));
            }

            if (string.IsNullOrWhiteSpace(request.Body))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "邮件正文不能为空"));
            }

            try
            {
                await emailService.SendEmailAsync(request.To, request.Subject, $"<pre>{WebUtility.HtmlEncode(request.Body)}</pre>");
                return Results.Ok(new ApiResponse(ResultCodes.EmailSendSuccess, "邮件发送成功"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send test email to {To}", request.To);
                return Results.BadRequest(new ApiResponse(ResultCodes.EmailSendFail, "邮件发送失败，请检查 SMTP 配置"));
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.AdminServer);

        // 重置指定用户密码（供管理前端使用；不传新密码时由服务端生成并一次性返回）
        app.MapPost("/api/admin/users/{id}/reset-password", async (
            ulong id,
            [FromBody] ResetPasswordRequest request,
            [FromServices] IUserService userService,
            [FromServices] ISessionService sessionService,
            [FromServices] IUserGroupService userGroupService,
            [FromServices] AppConfiguration appConfig,
            [FromServices] ILogger<Program> logger,
            HttpContext httpContext) =>
        {
            // 保护系统初始用户 (uid=0)
            if (id == 0)
            {
                return Results.Json(
                    new ApiResponse(ResultCodes.UpdateUserFail_Forbidden, "系统初始用户密码不可重置"),
                    statusCode: 403);
            }

            // 权限级别校验：不能重置权限级别不低于自己的用户的密码（重置即等于接管账号）
            var privilegeResult = await httpContext.ValidatePrivilegeLevelAsync(userGroupService, id);
            if (privilegeResult is not null) return privilegeResult;

            var user = await userService.GetByIdAsync(id);
            if (user == null || user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));
            }

            string? generatedPassword = null;
            var newPassword = request?.NewPassword;

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                generatedPassword = PasswordGenerator.Generate(appConfig.AppSettings.AdminPasswordLength);
                newPassword = generatedPassword;
            }
            else if (newPassword.Length is < 8 or > 128)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "密码长度需为 8-128 个字符"));
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, 12);
            await userService.UpdateUserAsync(user);

            // 强制目标用户重新登录：撤销其全部会话（所有设备）
            await sessionService.RevokeAllAsync(id, SessionRevokeReasons.PasswordReset);

            // 安全：不记录明文密码，仅记录操作
            logger.LogWarning("[RESET-PASSWORD] Password reset for user {UserId} by an administrator", id);

            return Results.Ok(new ApiResponse(ResultCodes.ChangePasswordSuccess, "密码已重置",
                new ResetPasswordResponse(user.Id, user.Username, generatedPassword)));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserResetPassword);

        // 管理员创建用户（不发送验证邮件；密码留空时由服务端生成并一次性返回）
        app.MapPost("/api/admin/users", async (
            [FromBody] AdminCreateUserRequest request,
            [FromServices] IUserService userService,
            [FromServices] IUserGroupService userGroupService,
            [FromServices] AppConfiguration appConfig,
            [FromServices] ILogger<Program> logger,
            HttpContext httpContext) =>
        {
            var actorId = httpContext.User.GetUserId();
            if (actorId is null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            var username = request.Username?.Trim();
            var email = request.Email?.Trim();

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "用户名与邮箱为必填项"));
            }

            if (username.Length is < 3 or > 64)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "用户名长度需为 3-64 个字符"));
            }

            if (await userService.ActiveUsernameExistsAsync(username))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_UsernameExists, "用户名已存在"));
            }

            if (await userService.ActiveEmailExistsAsync(email))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_EmailExists, "邮箱已被使用"));
            }

            // 与"权限组分配"端点同一规则：只能分配级别严格低于自己的权限组
            var actorLevel = await userGroupService.GetUserPrivilegeLevelAsync(actorId.Value);
            if (request.GroupIds is { Count: > 0 })
            {
                var levels = await userGroupService.GetGroupLevelsAsync(request.GroupIds);
                var illegal = levels
                    .Where(pair => !PrivilegeLevelExtensions.CanManageGroup(actorLevel, pair.Value))
                    .Select(pair => pair.Key)
                    .ToList();

                if (illegal.Count > 0)
                {
                    return Results.Json(new ApiResponse(ResultCodes.UpdateUserFail_PrivilegeLevel,
                            $"不能分配级别高于或等于自身的权限组（自身 {actorLevel}，不可分配 {string.Join("/", illegal)}）"),
                        statusCode: 403);
                }
            }

            var generated = string.IsNullOrWhiteSpace(request.Password);
            var password = generated
                ? PasswordGenerator.Generate(appConfig.AppSettings.AdminPasswordLength)
                : request.Password!.Trim();

            if (password.Length is < 8 or > 128)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "密码长度需为 8-128 个字符"));
            }

            var user = new User
            {
                Username = username,
                Email = email,
                Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 12),
                Status = request.RequireEmailVerification ? UserStatus.Unverified : UserStatus.Normal,
                EmailVerifiedAt = request.RequireEmailVerification ? null : DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            await userService.CreateUserAsync(user);

            if (request.GroupIds is { Count: > 0 })
            {
                await userGroupService.SetUserGroupsAsync(user.Id, request.GroupIds);
            }

            logger.LogInformation("[CREATE-USER] User {UserId} created by administrator {ActorId}",
                user.Id, actorId.Value);

            return Results.Ok(new ApiResponse(ResultCodes.Success, "用户已创建",
                new AdminCreateUserResponse(user.Id, user.Username, user.Email, generated ? password : null)));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserCreate);

        // 服务器历史指标（降采样分桶均值，供状态页折线图）
        app.MapGet("/api/admin/server/metrics", async (
            [FromQuery] int hours,
            [FromServices] ApplicationDbContext context) =>
        {
            if (hours is not (6 or 24 or 72 or 168))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "hours 仅支持 6 / 24 / 72 / 168"));
            }

            // 分桶策略：目标点数 ≤336，避免 SVG 渲染上万原始点
            var bucketMinutes = hours switch
            {
                6 => 2,
                24 => 5,
                72 => 15,
                _ => 30
            };

            var sinceUtc = DateTime.UtcNow.AddHours(-hours);
            var rows = await context.ServerMetrics
                .AsNoTracking()
                .Where(m => m.RecordedAt >= sinceUtc)
                .OrderBy(m => m.RecordedAt)
                .ToListAsync();

            // 按本地时间对齐桶起点（bucketStart 为本地时刻），桶内取均值
            var bucketSize = TimeSpan.FromMinutes(bucketMinutes);
            var buckets = rows
                .GroupBy(m => new DateTime(
                    m.RecordedAt.ToLocalTime().Ticks / bucketSize.Ticks * bucketSize.Ticks,
                    DateTimeKind.Local))
                .OrderBy(group => group.Key)
                .Select(group => new ServerMetricPoint(
                    group.Key.ToString(hours <= 24 ? "HH:mm" : "MM-dd HH:mm"),
                    Math.Round(group.Average(m => m.CpuUsage), 2),
                    Math.Round(group.Average(m => m.MemoryUsage), 2),
                    Math.Round(group.Average(m => m.MemoryUsedMb), 2),
                    Math.Round(group.Average(m => m.NetworkInBps), 2),
                    Math.Round(group.Average(m => m.NetworkOutBps), 2)))
                .ToList();

            return Results.Ok(new ApiResponse(ResultCodes.AdminStatsSuccess, "获取成功",
                new ServerMetricsData(hours, bucketMinutes, buckets)));
        }).RequireAuthorization().RequirePermission(PermissionCodes.AdminServer);

        // 运行日志（内存环形缓冲增量拉取，供「运行日志」页面；历史日志见 logs/ 目录文件）
        app.MapGet("/api/admin/logs", (
            [FromQuery] long afterId,
            [FromQuery] string? level,
            [FromQuery] int limit) =>
        {
            if (limit is < 10 or > 1000)
            {
                limit = 300;
            }

            var entries = MemoryLogSink.Instance.ReadAfter(afterId, level);

            // 首次拉取（afterId=0）取缓冲区尾部，避免一次灌入全量 2000 条
            var slice = afterId == 0
                ? entries.TakeLast(limit).ToList()
                : entries.Take(limit).ToList();

            var items = slice.Select(entry => new RuntimeLogEntry(
                entry.Sequence,
                entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                entry.Level,
                entry.Message,
                entry.Exception)).ToList();

            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功",
                new RuntimeLogsData(items, items.Count > 0 ? items[^1].Sequence : afterId)));
        }).RequireAuthorization().RequirePermission(PermissionCodes.AdminServer);
    }

    /// <summary>逐日聚合查询的原生 SQL 行形状（SqlQuery 按列名映射）。</summary>
    public sealed record DailyCountRow(DateTime Date, int Count);
}
