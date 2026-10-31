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
        // 注意：未预期的异常由全局 ExceptionHandlingMiddleware 统一处理为 500

        // 获取系统基本信息
        app.MapGet("/api/system/info", async (
            [FromServices] ISystemSettingService settingService) =>
        {
            var info = await settingService.GetSystemInfoAsync();
            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", info));
        });

        // ============ 管理接口（需要权限）============

        // 更新系统基本信息
        app.MapPut("/api/admin/system/info", async (
            [FromBody] UpdateSystemInfoRequest request,
            ClaimsPrincipal currentUser,
            [FromServices] ISystemSettingService settingService) =>
        {
            var userId = currentUser.GetRequiredUserId();
            var result = await settingService.UpdateSystemInfoAsync(request, userId);
            return Results.Ok(new ApiResponse(ResultCodes.Success, "更新成功", result));
        }).RequireAuthorization().RequirePermission(PermissionCodes.SystemSettingUpdate);

        // 获取所有设置
        app.MapGet("/api/admin/system/settings", async (
            [FromServices] ISystemSettingService settingService) =>
        {
            var settings = await settingService.GetAllSettingsAsync();
            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", settings));
        }).RequireAuthorization().RequirePermission(PermissionCodes.SystemSettingRead);

        // 获取单个设置
        app.MapGet("/api/admin/system/settings/{key}", async (
            string key,
            [FromServices] ISystemSettingService settingService) =>
        {
            var setting = await settingService.GetSettingAsync(key);
            return setting != null
                ? Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", setting))
                : Results.NotFound(new ApiResponse(ResultCodes.NotFound, "设置不存在"));
        }).RequireAuthorization().RequirePermission(PermissionCodes.SystemSettingRead);

        // 更新单个设置
        app.MapPut("/api/admin/system/settings/{key}", async (
            string key,
            [FromBody] UpdateSettingRequest request,
            ClaimsPrincipal currentUser,
            [FromServices] ISystemSettingService settingService) =>
        {
            var userId = currentUser.GetRequiredUserId();
            var result = await settingService.UpdateSettingAsync(key, request, userId);
            return Results.Ok(new ApiResponse(ResultCodes.Success, "更新成功", result));
        }).RequireAuthorization().RequirePermission(PermissionCodes.SystemSettingUpdate);
    }
}
