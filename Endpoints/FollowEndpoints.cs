using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using UserManagementSystem.DTOs;
using UserManagementSystem.Extensions;
using UserManagementSystem.Services;

namespace UserManagementSystem.Endpoints;

public static class FollowEndpoints
{
    private static ulong GetCurrentUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !ulong.TryParse(userIdClaim.Value, out var userId))
        {
            throw new UnauthorizedAccessException("无法获取当前用户信息");
        }
        return userId;
    }

    public static void MapFollowEndpoints(this WebApplication app)
    {
        // ============ 关注相关端点 ============

        // 关注用户
        app.MapPost("/api/users/{userId}/follow", async (
            ulong userId,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService,
            [FromServices] ILogger<Program> logger) =>
        {
            try
            {
                var currentUserId = GetCurrentUserId(currentUser);
                var result = await followService.FollowAsync(currentUserId, userId);
                return Results.Ok(new ApiResponse(0, "关注成功", result));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ApiResponse(400, ex.Message));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new ApiResponse(404, "用户不存在"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to follow user {UserId}", userId);
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowUser);

        // 取消关注
        app.MapDelete("/api/users/{userId}/follow", async (
            ulong userId,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService,
            [FromServices] ILogger<Program> logger) =>
        {
            try
            {
                var currentUserId = GetCurrentUserId(currentUser);
                var result = await followService.UnfollowAsync(currentUserId, userId);
                if (!result)
                {
                    return Results.BadRequest(new ApiResponse(400, "未关注该用户"));
                }
                return Results.Ok(new ApiResponse(0, "取消关注成功"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to unfollow user {UserId}", userId);
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowUser);

        // 检查是否已关注
        app.MapGet("/api/users/{userId}/follow/status", async (
            ulong userId,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            try
            {
                var currentUserId = GetCurrentUserId(currentUser);
                var isFollowing = await followService.IsFollowingAsync(currentUserId, userId);
                return Results.Ok(new ApiResponse(0, "获取成功", new { IsFollowing = isFollowing }));
            }
            catch (Exception)
            {
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowRead);

        // 获取我的关注列表
        app.MapGet("/api/users/me/following", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            try
            {
                var currentUserId = GetCurrentUserId(currentUser);
                page = page <= 0 ? 1 : page;
                pageSize = pageSize <= 0 || pageSize > 100 ? 20 : pageSize;

                var result = await followService.GetFollowingListAsync(currentUserId, page, pageSize, currentUserId);
                return Results.Ok(new ApiResponse(0, "获取成功", result));
            }
            catch (Exception)
            {
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowRead);

        // 获取我的粉丝列表
        app.MapGet("/api/users/me/followers", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            try
            {
                var currentUserId = GetCurrentUserId(currentUser);
                page = page <= 0 ? 1 : page;
                pageSize = pageSize <= 0 || pageSize > 100 ? 20 : pageSize;

                var result = await followService.GetFollowersListAsync(currentUserId, page, pageSize, currentUserId);
                return Results.Ok(new ApiResponse(0, "获取成功", result));
            }
            catch (Exception)
            {
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowRead);

        // 获取指定用户的关注列表（受隐私设置影响）
        app.MapGet("/api/users/{userId}/following", async (
            ulong userId,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            try
            {
                var currentUserId = GetCurrentUserId(currentUser);
                page = page <= 0 ? 1 : page;
                pageSize = pageSize <= 0 || pageSize > 100 ? 20 : pageSize;

                var result = await followService.GetFollowingListAsync(userId, page, pageSize, currentUserId);
                return Results.Ok(new ApiResponse(0, "获取成功", result));
            }
            catch (Exception)
            {
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowRead);

        // 获取指定用户的粉丝列表
        app.MapGet("/api/users/{userId}/followers", async (
            ulong userId,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            try
            {
                var currentUserId = GetCurrentUserId(currentUser);
                page = page <= 0 ? 1 : page;
                pageSize = pageSize <= 0 || pageSize > 100 ? 20 : pageSize;

                var result = await followService.GetFollowersListAsync(userId, page, pageSize, currentUserId);
                return Results.Ok(new ApiResponse(0, "获取成功", result));
            }
            catch (Exception)
            {
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowRead);

        // 获取关注统计
        app.MapGet("/api/users/{userId}/follow/stats", async (
            ulong userId,
            [FromServices] IFollowService followService) =>
        {
            try
            {
                var result = await followService.GetFollowStatsAsync(userId);
                return Results.Ok(new ApiResponse(0, "获取成功", result));
            }
            catch (Exception)
            {
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowRead);

        // 获取我的关注统计
        app.MapGet("/api/users/me/follow/stats", async (
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            try
            {
                var currentUserId = GetCurrentUserId(currentUser);
                var result = await followService.GetFollowStatsAsync(currentUserId);
                return Results.Ok(new ApiResponse(0, "获取成功", result));
            }
            catch (Exception)
            {
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowRead);

        // 设置关注列表可见性
        app.MapPut("/api/users/me/following/visibility", async (
            [FromBody] VisibilityRequest request,
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            try
            {
                var currentUserId = GetCurrentUserId(currentUser);
                await followService.SetFollowingListVisibilityAsync(currentUserId, request.IsHidden);
                return Results.Ok(new ApiResponse(0, "设置成功"));
            }
            catch (Exception)
            {
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowManage);

        // 获取关注列表可见性设置
        app.MapGet("/api/users/me/following/visibility", async (
            ClaimsPrincipal currentUser,
            [FromServices] IFollowService followService) =>
        {
            try
            {
                var currentUserId = GetCurrentUserId(currentUser);
                var isHidden = await followService.GetFollowingListVisibilityAsync(currentUserId);
                return Results.Ok(new ApiResponse(0, "获取成功", new { IsHidden = isHidden }));
            }
            catch (Exception)
            {
                return Results.StatusCode(500);
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.FollowManage);
    }
}

public record VisibilityRequest(bool IsHidden);
