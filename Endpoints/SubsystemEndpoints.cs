using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using UserManagementSystem.Auth;
using UserManagementSystem.DTOs;
using UserManagementSystem.Extensions;
using UserManagementSystem.Models;
using UserManagementSystem.Services;

namespace UserManagementSystem.Endpoints;

/// <summary>
/// 子系统凭证管理端点：登记子系统、签发/重置凭证、启停与删除。
/// 明文 Secret 仅在创建或重置时返回一次。
/// 另含子系统凭凭证自注册权限的端点（仅接受子系统凭证，用户 JWT 无法访问）。
/// </summary>
public static class SubsystemEndpoints
{
    public static void MapSubsystemEndpoints(this WebApplication app)
    {
        // 子系统凭凭证自注册/全量同步本子系统的权限码（仅需认证的子系统专用端点）
        app.MapPut("/api/subsystems/self/permissions", async (
            [FromBody] SubsystemPermissionSyncRequest request,
            [FromServices] IPermissionService permissionService,
            HttpContext httpContext) =>
        {
            var idClaim = httpContext.User.FindFirstValue(SubsystemKeyAuthenticationHandler.SubsystemIdClaim);
            var codeClaim = httpContext.User.FindFirstValue(SubsystemKeyAuthenticationHandler.SubsystemCodeClaim);
            if (!ulong.TryParse(idClaim, out var subsystemId) || string.IsNullOrWhiteSpace(codeClaim))
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "子系统凭证无效"), statusCode: 401);
            }

            var result = await permissionService.SyncSubsystemPermissionsAsync(subsystemId, codeClaim, request.Codes);

            if (result.Conflicts.Count > 0)
            {
                return Results.Json(new ApiResponse(
                    ResultCodes.SubsystemPermissionConflict,
                    "部分权限码格式非法或已归属他方，本次同步未提交",
                    new SubsystemPermissionSyncResponse(0, 0, 0, result.Conflicts)), statusCode: 409);
            }

            return Results.Ok(new ApiResponse(ResultCodes.SubsystemPermissionSyncSuccess, "同步成功",
                new SubsystemPermissionSyncResponse(result.Added, result.Updated, result.Deleted, result.Conflicts)));
        }).RequireAuthorization(AuthPolicies.SubsystemKey);
        // 子系统列表（分页 + 关键字搜索）
        app.MapGet("/api/subsystems", async (
            [FromServices] ISubsystemService subsystemService,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null) =>
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100; // 与其他分页接口统一：越界截断到 100

            var (items, totalCount) = await subsystemService.GetPagedAsync(page, pageSize, search);

            var response = new PagedApiResponse(
                ResultCodes.Success, "获取成功",
                items.Select(s => (object)ToResponse(s)).ToList(),
                page, pageSize, totalCount);

            return Results.Ok(response);
        }).RequireAuthorization().RequirePermission(PermissionCodes.SubsystemRead);

        // 子系统详情
        app.MapGet("/api/subsystems/{id}", async (
            ulong id,
            [FromServices] ISubsystemService subsystemService) =>
        {
            var entry = await subsystemService.GetByIdAsync(id);
            if (entry is null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.NotFound, "子系统不存在"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", ToResponse(entry)));
        }).RequireAuthorization().RequirePermission(PermissionCodes.SubsystemRead);

        // 创建子系统并签发凭证（Secret 仅此一次返回）
        app.MapPost("/api/subsystems", async (
            [FromBody] CreateSubsystemRequest request,
            [FromServices] ISubsystemService subsystemService,
            HttpContext httpContext) =>
        {
            var operatorId = httpContext.User.GetUserId();
            if (operatorId is null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            // 权限码不再由管理员代为声明：子系统登记后凭凭证自行注册
            var (entry, plainSecret) = await subsystemService.CreateAsync(request, operatorId.Value);

            return Results.Ok(new ApiResponse(ResultCodes.Success, "创建成功，请立即保存密钥（仅显示一次）",
                new SubsystemCredentialResponse(ToResponse(entry), plainSecret)));
        }).RequireAuthorization().RequirePermission(PermissionCodes.SubsystemCreate);

        // 更新子系统元数据（不影响凭证）
        app.MapPut("/api/subsystems/{id}", async (
            ulong id,
            [FromBody] UpdateSubsystemRequest request,
            [FromServices] ISubsystemService subsystemService,
            HttpContext httpContext) =>
        {
            var operatorId = httpContext.User.GetUserId();
            if (operatorId is null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            var entry = await subsystemService.UpdateAsync(id, request, operatorId.Value);
            if (entry is null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.NotFound, "子系统不存在"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.Success, "更新成功", ToResponse(entry)));
        }).RequireAuthorization().RequirePermission(PermissionCodes.SubsystemUpdate);

        // 启用/停用子系统
        app.MapPut("/api/subsystems/{id}/status", async (
            ulong id,
            [FromBody] SetSubsystemStatusRequest request,
            [FromServices] ISubsystemService subsystemService,
            HttpContext httpContext) =>
        {
            var operatorId = httpContext.User.GetUserId();
            if (operatorId is null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            var entry = await subsystemService.SetStatusAsync(id, request.IsEnabled, operatorId.Value);
            if (entry is null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.NotFound, "子系统不存在"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.Success,
                request.IsEnabled ? "已启用" : "已停用", ToResponse(entry)));
        }).RequireAuthorization().RequirePermission(PermissionCodes.SubsystemUpdate);

        // 重新生成凭证（旧凭证立即失效，新 Secret 仅此一次返回）
        app.MapPost("/api/subsystems/{id}/regenerate-secret", async (
            ulong id,
            [FromServices] ISubsystemService subsystemService,
            HttpContext httpContext) =>
        {
            var operatorId = httpContext.User.GetUserId();
            if (operatorId is null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            var result = await subsystemService.RegenerateSecretAsync(id, operatorId.Value);
            if (result is null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.NotFound, "子系统不存在"));
            }

            var (entry, plainSecret) = result.Value;
            return Results.Ok(new ApiResponse(ResultCodes.Success, "密钥已重新生成，原密钥立即失效，请保存新密钥（仅显示一次）",
                new SubsystemCredentialResponse(ToResponse(entry), plainSecret)));
        }).RequireAuthorization().RequirePermission(PermissionCodes.SubsystemUpdate);

        // 删除子系统（凭证同时失效）
        app.MapDelete("/api/subsystems/{id}", async (
            ulong id,
            [FromServices] ISubsystemService subsystemService) =>
        {
            var deleted = await subsystemService.DeleteAsync(id);
            if (!deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.NotFound, "子系统不存在"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.Success, "删除成功"));
        }).RequireAuthorization().RequirePermission(PermissionCodes.SubsystemDelete);
    }

    private static SubsystemResponse ToResponse(Subsystem entry) => new(
        entry.Id,
        entry.Name,
        entry.Code,
        entry.Description,
        entry.HomeUrl,
        entry.IsEnabled,
        entry.ApiKey,
        entry.CreatedAt,
        entry.UpdatedAt,
        entry.CreatedBy,
        entry.LastUsedAt,
        entry.ExpiresAt);
}
