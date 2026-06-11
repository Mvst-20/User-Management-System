using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using UserManagementSystem.DTOs;
using UserManagementSystem.Extensions;
using UserManagementSystem.Services;

namespace UserManagementSystem.Endpoints;

public static class SystemSettingEndpoints
{
    public static void MapSystemSettingEndpoints(this WebApplication app)
    {
        // ============ 公开接口（不需要认证）============

        // 获取系统基本信息
        app.MapGet("/api/system/info", async (
            [FromServices] ISystemSettingService settingService,
            [FromServices] ILogger<Program> logger) =>
        {
            try
            {
                var info = await settingService.GetSystemInfoAsync();
                return Results.Ok(new ApiResponse(0, "获取成功", info));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get system info");
                return Results.StatusCode(500);
            }
        });

        // 获取友情链接
        app.MapGet("/api/system/friend-links", async (
            [FromServices] ISystemSettingService settingService,
            [FromServices] ILogger<Program> logger) =>
        {
            try
            {
                var links = await settingService.GetFriendLinksAsync();
                return Results.Ok(new ApiResponse(0, "获取成功", links));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get friend links");
                return Results.StatusCode(500);
            }
        });

        // ============ 管理接口（需要权限）============

        // 更新系统基本信息
        app.MapPut("/api/admin/system/info", async (
            [FromBody] UpdateSystemInfoRequest request,
            ClaimsPrincipal currentUser,
            [FromServices] ISystemSettingService settingService,
            [FromServices] ILogger<Program> logger) =>
        {
            try
            {
                var userId = currentUser.GetRequiredUserId();
                var result = await settingService.UpdateSystemInfoAsync(request, userId);
                return Results.Ok(new ApiResponse(0, "更新成功", result));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update system info");
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.SystemSettingUpdate);

        // 更新友情链接
        app.MapPut("/api/admin/system/friend-links", async (
            [FromBody] UpdateFriendLinksRequest request,
            ClaimsPrincipal currentUser,
            [FromServices] ISystemSettingService settingService,
            [FromServices] ILogger<Program> logger) =>
        {
            try
            {
                var userId = currentUser.GetRequiredUserId();
                var result = await settingService.UpdateFriendLinksAsync(request, userId);
                return Results.Ok(new ApiResponse(0, "更新成功", result));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update friend links");
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.SystemSettingUpdate);

        // 获取所有设置
        app.MapGet("/api/admin/system/settings", async (
            [FromServices] ISystemSettingService settingService,
            [FromServices] ILogger<Program> logger) =>
        {
            try
            {
                var settings = await settingService.GetAllSettingsAsync();
                return Results.Ok(new ApiResponse(0, "获取成功", settings));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get all settings");
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.SystemSettingRead);

        // 获取单个设置
        app.MapGet("/api/admin/system/settings/{key}", async (
            string key,
            [FromServices] ISystemSettingService settingService,
            [FromServices] ILogger<Program> logger) =>
        {
            try
            {
                var setting = await settingService.GetSettingAsync(key);
                return setting != null
                    ? Results.Ok(new ApiResponse(0, "获取成功", setting))
                    : Results.NotFound(new ApiResponse(404, "设置不存在"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get setting {Key}", key);
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.SystemSettingRead);

        // 更新单个设置
        app.MapPut("/api/admin/system/settings/{key}", async (
            string key,
            [FromBody] UpdateSettingRequest request,
            ClaimsPrincipal currentUser,
            [FromServices] ISystemSettingService settingService,
            [FromServices] ILogger<Program> logger) =>
        {
            try
            {
                var userId = currentUser.GetRequiredUserId();
                var result = await settingService.UpdateSettingAsync(key, request, userId);
                return Results.Ok(new ApiResponse(0, "更新成功", result));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update setting {Key}", key);
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.SystemSettingUpdate);
    }
}
