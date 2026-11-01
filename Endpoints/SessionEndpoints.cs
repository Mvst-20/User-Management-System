using Microsoft.AspNetCore.Mvc;
using UserManagementSystem.DTOs;
using UserManagementSystem.Extensions;
using UserManagementSystem.Services;
using UserManagementSystem.Services.Security;

namespace UserManagementSystem.Endpoints;

/// <summary>
/// 会话（已登录设备）与登录历史端点。
/// <para>
/// 自助接口按 <c>session:read</c> / <c>session:revoke</c> / <c>login-history:read</c> 授权，
/// 默认包含在「普通用户」组内；管理员接口复用 <c>user:read</c> / <c>user:update</c>。
/// </para>
/// </summary>
public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this WebApplication app)
    {
        // ============ 自助：我的登录设备 ============

        app.MapGet("/api/users/me/sessions", async (
            [FromServices] ISessionService sessionService,
            HttpContext httpContext) =>
        {
            var userId = httpContext.User.GetUserId();
            if (userId == null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未登录"), statusCode: 401);
            }

            var currentSessionId = httpContext.User.GetSessionId();
            var items = await sessionService.GetSessionsAsync(userId.Value, currentSessionId);

            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功",
                new SessionListResponse(items, items.Count)));
        }).RequireAuthorization().RequirePermission(PermissionCodes.SessionRead);

        // 踢出指定设备
        app.MapDelete("/api/users/me/sessions/{sessionId}", async (
            string sessionId,
            [FromServices] ISessionService sessionService,
            HttpContext httpContext) =>
        {
            var userId = httpContext.User.GetUserId();
            if (userId == null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未登录"), statusCode: 401);
            }

            // 撤销操作由服务层校验会话归属，防止越权踢出他人会话
            var revoked = await sessionService.RevokeSessionAsync(
                userId.Value, sessionId, SessionRevokeReasons.RevokedByUser);

            return revoked
                ? Results.Ok(new ApiResponse(ResultCodes.Success, "该设备已退出登录"))
                : Results.NotFound(new ApiResponse(ResultCodes.NotFound, "会话不存在或已失效"));
        }).RequireAuthorization().RequirePermission(PermissionCodes.SessionRevoke);

        // 一键踢出其他全部设备（保留当前设备）
        app.MapDelete("/api/users/me/sessions", async (
            [FromServices] ISessionService sessionService,
            HttpContext httpContext) =>
        {
            var userId = httpContext.User.GetUserId();
            if (userId == null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未登录"), statusCode: 401);
            }

            var currentSessionId = httpContext.User.GetSessionId();
            var count = await sessionService.RevokeAllExceptAsync(
                userId.Value, currentSessionId, SessionRevokeReasons.LogoutAll);

            return Results.Ok(new ApiResponse(ResultCodes.Success,
                count > 0 ? $"已踢出 {count} 台设备" : "没有其他在线设备", new { revoked = count }));
        }).RequireAuthorization().RequirePermission(PermissionCodes.SessionRevoke);

        // ============ 自助：登录历史 ============

        app.MapGet("/api/users/me/login-history", async (
            [FromServices] ISessionService sessionService,
            HttpContext httpContext,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] bool? success = null,
            [FromQuery] string? eventType = null) =>
        {
            var userId = httpContext.User.GetUserId();
            if (userId == null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未登录"), statusCode: 401);
            }

            // eventType 白名单校验：alert 属于内部提醒记录，不对外暴露；
            // 非法值返回参数错误，而不是 200 + 空列表。
            if (!string.IsNullOrWhiteSpace(eventType))
            {
                eventType = eventType.Trim().ToLowerInvariant();
                if (!LoginLogEvents.VisibleEventTypes.Contains(eventType))
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "eventType 参数无效"));
                }
            }

            var history = await sessionService.GetLoginHistoryAsync(userId.Value, page, pageSize, success, eventType);
            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", history));        }).RequireAuthorization().RequirePermission(PermissionCodes.LoginHistoryRead);

        // ============ 管理员：查看/踢出指定用户的设备 ============

        app.MapGet("/api/admin/users/{id}/sessions", async (
            ulong id,
            [FromServices] ISessionService sessionService) =>
        {
            var items = await sessionService.GetSessionsAsync(id, null);
            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功",
                new SessionListResponse(items, items.Count)));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserRead);

        app.MapDelete("/api/admin/users/{id}/sessions/{sessionId}", async (
            ulong id,
            string sessionId,
            [FromServices] ISessionService sessionService) =>
        {
            var revoked = await sessionService.RevokeSessionAsync(
                id, sessionId, SessionRevokeReasons.RevokedByAdmin);

            return revoked
                ? Results.Ok(new ApiResponse(ResultCodes.Success, "该设备已被踢出"))
                : Results.NotFound(new ApiResponse(ResultCodes.NotFound, "会话不存在或已失效"));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserUpdate);
    }
}
