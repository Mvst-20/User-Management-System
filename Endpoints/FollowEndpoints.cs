using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using UserManagementSystem.DTOs;
using UserManagementSystem.Exceptions;
using UserManagementSystem.Extensions;
using UserManagementSystem.Services;

namespace UserManagementSystem.Endpoints;

public static class FollowEndpoints
{
    public static void MapFollowEndpoints(this WebApplication app)
    {
        // ============ 关注相关端点 ============
        // 注意：未预期的异常由全局 ExceptionHandlingMiddleware 统一处理为 500

        // 关注用户
        app.MapPost("/api/users/{userId}/follow", async (
            ulong userId,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            try
            {
                var uid = currentUser.GetRequiredUserId();
                var result = await followService.FollowAsync(uid, userId);
                return Results.Ok(new ApiResponse(ResultCodes.Success, "关注成功", result));
            }
            catch (BusinessException ex)
            {
                return Results.Json(new ApiResponse(ex.Code, ex.Message), statusCode: ex.StatusCode);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowUser);

        // 取消关注
        app.MapDelete("/api/users/{userId}/follow", async (
            ulong userId,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            var uid = currentUser.GetRequiredUserId();
            var result = await followService.UnfollowAsync(uid, userId);
            return result
                ? Results.Ok(new ApiResponse(ResultCodes.Success, "取消关注成功"))
                : Results.BadRequest(new ApiResponse(ResultCodes.Error, "未关注该用户"));
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowUser);

        // 检查是否已关注
        app.MapGet("/api/users/{userId}/follow/status", async (
            ulong userId,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            var uid = currentUser.GetRequiredUserId();
            var isFollowing = await followService.IsFollowingAsync(uid, userId);
            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", new { IsFollowing = isFollowing }));
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowRead);

        // 获取我的关注列表
        app.MapGet("/api/users/me/following", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            var uid = currentUser.GetRequiredUserId();
            (page, pageSize) = UserExtensions.NormalizePagination(page, pageSize);
            var result = await followService.GetFollowingListAsync(uid, page, pageSize, uid);
            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", result));
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowRead);

        // 获取我的粉丝列表
        app.MapGet("/api/users/me/followers", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            var uid = currentUser.GetRequiredUserId();
            (page, pageSize) = UserExtensions.NormalizePagination(page, pageSize);
            var result = await followService.GetFollowersListAsync(uid, page, pageSize, uid);
            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", result));
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowRead);

        // 获取指定用户的关注列表（受隐私设置影响）
        app.MapGet("/api/users/{userId}/following", async (
            ulong userId,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            var uid = currentUser.GetRequiredUserId();
            (page, pageSize) = UserExtensions.NormalizePagination(page, pageSize);
            var result = await followService.GetFollowingListAsync(userId, page, pageSize, uid);
            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", result));
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowRead);

        // 获取指定用户的粉丝列表
        app.MapGet("/api/users/{userId}/followers", async (
            ulong userId,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            var uid = currentUser.GetRequiredUserId();
            (page, pageSize) = UserExtensions.NormalizePagination(page, pageSize);
            var result = await followService.GetFollowersListAsync(userId, page, pageSize, uid);
            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", result));
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowRead);

        // 获取关注统计
        app.MapGet("/api/users/{userId}/follow/stats", async (
            ulong userId,
            [FromServices] IFollowService followService) =>
        {
            var result = await followService.GetFollowStatsAsync(userId);
            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", result));
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowRead);

        // 获取我的关注统计
        app.MapGet("/api/users/me/follow/stats", async (
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            var uid = currentUser.GetRequiredUserId();
            var result = await followService.GetFollowStatsAsync(uid);
            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", result));
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowRead);

        // 设置关注列表可见性
        app.MapPut("/api/users/me/following/visibility", async (
            [FromBody] VisibilityRequest request,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            var uid = currentUser.GetRequiredUserId();
            await followService.SetFollowingListVisibilityAsync(uid, request.IsHidden);
            return Results.Ok(new ApiResponse(ResultCodes.Success, "设置成功"));
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowManage);

        // 获取关注列表可见性设置
        app.MapGet("/api/users/me/following/visibility", async (
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            var uid = currentUser.GetRequiredUserId();
            var isHidden = await followService.GetFollowingListVisibilityAsync(uid);
            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", new { IsHidden = isHidden }));
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowManage);
    }
}

public record VisibilityRequest(bool IsHidden);
