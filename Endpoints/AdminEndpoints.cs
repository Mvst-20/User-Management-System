using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Services;

namespace UserManagementSystem.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        // 在线用户数量
        app.MapGet("/api/admin/stats/online-users", (
            [FromServices] IUserService userService) =>
        {
            var count = userService.GetOnlineUsersCount();
            return Results.Ok(new ApiResponse(ResultCodes.AdminStatsSuccess, "获取成功", new { OnlineUsers = count }));
        }).RequireAuthorization("AdminOnly");

        // 新注册用户数量（支持 days 参数）
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
        }).RequireAuthorization("AdminOnly");

        // 活跃用户数量（支持 days 参数）
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
        }).RequireAuthorization("AdminOnly");

        // 服务器状态
        app.MapGet("/api/admin/server/status", async (
            [FromServices] IServiceProvider serviceProvider) =>
        {
            var startTime = Process.GetCurrentProcess().StartTime;
            var uptime = DateTime.UtcNow - startTime;

            var process = Process.GetCurrentProcess();
            var cpuTime = process.TotalProcessorTime;
            var cpuUsage = cpuTime.TotalMilliseconds / (Environment.ProcessorCount * 1000.0);

            var memoryUsed = process.WorkingSet64;
            var memoryTotal = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

            // 检查数据库连接
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
                $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s",
                Math.Round(cpuUsage, 2),
                Math.Round((double)memoryUsed / memoryTotal * 100, 2),
                memoryUsed,
                memoryTotal,
                DateTime.UtcNow,
                dbStatus,
                "1.0.0"
            );

            return Results.Ok(new ApiResponse(ResultCodes.ServerStatusSuccess, "获取成功", response));
        }).RequireAuthorization("AdminOnly");

        // 发送测试邮件
        app.MapPost("/api/admin/email/test", async (
            [FromBody] SendEmailRequest request,
            [FromServices] IEmailService emailService) =>
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
                await emailService.SendEmailAsync(request.To, request.Subject, $"<pre>{request.Body}</pre>");
                return Results.Ok(new ApiResponse(ResultCodes.EmailSendSuccess, "邮件发送成功"));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.EmailSendFail, $"邮件发送失败: {ex.Message}"));
            }
        }).RequireAuthorization("AdminOnly");
    }
}
