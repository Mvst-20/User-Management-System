using Microsoft.AspNetCore.Mvc;
using UserManagementSystem.Auth;
using UserManagementSystem.DTOs;
using UserManagementSystem.Extensions;
using UserManagementSystem.Models;
using UserManagementSystem.Services;

namespace UserManagementSystem.Endpoints;

public static class PermissionEndpoints
{
    public static void MapPermissionEndpoints(this WebApplication app)
    {
        // ============ 公开端点 ============

        // 获取自定义权限列表（公共，无需认证）
        app.MapGet("/api/permissions/custom", async (
            [FromServices] IPermissionService permissionService) =>
        {
            var permissions = await permissionService.GetCustomPermissionsAsync();
            var response = permissions.Select(p => new PermissionResponse(
                p.Id, p.Code, p.Name, p.Category, p.IsSpecial, p.IsSystem, p.Description, p.CreatorId, p.CreatedAt,
                p.SubsystemId, p.Subsystem?.Name
            )).ToList();

            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", response));
        });

        // ============ 需要认证的端点 ============

        // 获取所有权限列表
        app.MapGet("/api/permissions", async (
            [FromServices] IPermissionService permissionService) =>
        {
            var permissions = await permissionService.GetAllPermissionsAsync();
            var response = permissions.Select(p => new PermissionResponse(
                p.Id, p.Code, p.Name, p.Category, p.IsSpecial, p.IsSystem, p.Description, p.CreatorId, p.CreatedAt,
                p.SubsystemId, p.Subsystem?.Name
            )).ToList();

            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", response));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.PermissionRead);

        // 权限检查（支持单码 + 批量两种模式）
        app.MapPost("/api/permissions/check", async (
            [FromBody] CheckPermissionRequest request,
            [FromServices] IPermissionService permissionService,
            HttpContext httpContext) =>
        {
            var currentUserId = httpContext.User.GetUserId();

            // 子系统凭证调用（无用户上下文）：凭证即授权，但必须显式指定要检查的用户
            var isSubsystemCaller = httpContext.User.HasClaim(c => c.Type == SubsystemKeyAuthenticationHandler.SubsystemIdClaim);
            if (isSubsystemCaller && !request.UserId.HasValue)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "子系统调用必须显式传入 userId"));
            }

            // 确定要检查的用户 ID
            var targetUserId = request.UserId ?? currentUserId;
            if (!targetUserId.HasValue)
            {
                return Results.Unauthorized();
            }

            // 检查他人权限需要 permission:read 权限（子系统凭证已授权，跳过该内联校验）
            if (!isSubsystemCaller && request.UserId.HasValue && request.UserId.Value != currentUserId)
            {
                if (!currentUserId.HasValue || !await permissionService.HasPermissionAsync(currentUserId.Value, PermissionCodes.PermissionRead))
                {
                    return Results.Json(new ApiResponse(ResultCodes.Forbidden, "权限不足，查看他人权限需要 permission:read 权限"), statusCode: 403);
                }
            }

            // ── 批量模式（codes 非空，供 BWB/IHS 等子系统使用）──
            if (request.Codes is { Length: > 0 })
            {
                var userPermissionCodes = await permissionService.GetUserPermissionCodesAsync(targetUserId.Value);
                var items = request.Codes.Select(code => new PermissionCheckItem(
                    code,
                    HasPermissionCoverage(userPermissionCodes, code)
                )).ToList();

                return Results.Ok(new ApiResponse(ResultCodes.PermissionCheckSuccess, "检查完成",
                    new BulkCheckPermissionResponse(targetUserId.Value, items)));
            }

            // ── 单码模式（向后兼容）──
            if (string.IsNullOrEmpty(request.PermissionCode))
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "权限代码不能为空"));

            var hasPermission = await permissionService.HasPermissionAsync(targetUserId.Value, request.PermissionCode);

            return Results.Ok(new ApiResponse(ResultCodes.PermissionCheckSuccess, "检查完成",
                new CheckPermissionResponse(targetUserId.Value, request.PermissionCode, hasPermission)));
        })
        .RequireAuthorization();

        // 创建权限（B 权限覆盖，不能创建 ABC 特殊权限）
        app.MapPost("/api/permissions", async (
            [FromBody] CreatePermissionRequest request,
            [FromServices] IPermissionService permissionService,
            HttpContext httpContext) =>
        {
            var specialCodes = new[] { PermissionCodes.PermA, PermissionCodes.PermB, PermissionCodes.PermC };
            if (specialCodes.Contains(request.Code))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.PermissionCannotModifySpecial, "不能创建特殊权限"));
            }

            if (await permissionService.GetPermissionByCodeAsync(request.Code) != null)
            {
                return Results.Conflict(new ApiResponse(ResultCodes.PermissionCodeExists, "权限代码已存在"));
            }

            var currentUserId = httpContext.User.GetUserId();

            var permission = new Permission
            {
                Code = request.Code,
                Name = request.Name,
                Category = request.Category,
                IsSpecial = false,
                IsSystem = false,
                Description = request.Description,
                CreatorId = currentUserId,
                CreatedAt = DateTime.UtcNow
            };

            var created = await permissionService.CreatePermissionAsync(permission);
            var response = new PermissionResponse(created.Id, created.Code, created.Name, created.Category,
                created.IsSpecial, created.IsSystem, created.Description, created.CreatorId, created.CreatedAt);

            return Results.Created($"/api/permissions/{created.Id}",
                new ApiResponse(ResultCodes.PermissionCreateSuccess, "创建成功", response));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.PermissionCreate);

        // 更新权限（B 权限覆盖，不能修改系统内置权限）
        app.MapPut("/api/permissions/{id}", async (
            ulong id,
            [FromBody] UpdatePermissionRequest request,
            [FromServices] IPermissionService permissionService) =>
        {
            var permission = await permissionService.GetPermissionByIdAsync(id);
            if (permission == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.PermissionNotFound, "权限不存在"));
            }

            if (permission.IsSpecial)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.PermissionCannotModifySpecial, "不能修改特殊权限"));
            }

            if (permission.IsSystem)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.PermissionCannotModifySpecial, "系统内置权限不可修改"));
            }

            var updated = await permissionService.UpdatePermissionAsync(id, request.Name, request.Description);
            if (updated == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.PermissionNotFound, "权限不存在"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.PermissionUpdateSuccess, "更新成功",
                new PermissionResponse(updated.Id, updated.Code, updated.Name, updated.Category, updated.IsSpecial, updated.IsSystem, updated.Description, updated.CreatorId, updated.CreatedAt)));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.PermissionUpdate);

        // 删除权限（B 权限覆盖，不能删除系统内置权限）
        app.MapDelete("/api/permissions/{id}", async (
            ulong id,
            [FromServices] IPermissionService permissionService) =>
        {
            var permission = await permissionService.GetPermissionByIdAsync(id);
            if (permission == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.PermissionNotFound, "权限不存在"));
            }

            if (permission.IsSpecial)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.PermissionCannotModifySpecial, "不能删除特殊权限"));
            }

            if (permission.IsSystem)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.PermissionCannotModifySpecial, "系统内置权限不可删除"));
            }

            if (permission.SubsystemId is not null)
            {
                // 子系统注册的权限不可单独删除：移除途径是卸载对应子系统（级联删除）
                return Results.BadRequest(new ApiResponse(ResultCodes.PermissionCannotModifySpecial, "子系统注册的权限不可单独删除，请卸载对应子系统"));
            }

            var deleted = await permissionService.DeletePermissionAsync(id);
            if (!deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.PermissionNotFound, "权限不存在"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.PermissionDeleteSuccess, "删除成功"));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.PermissionDelete);
    }

    /// <summary>
    /// 判断用户权限列表中是否存在覆盖目标权限的权限。
    /// 与 <see cref="IPermissionService.HasPermissionAsync"/> 使用相同的 ABC 覆盖规则。
    /// </summary>
    private static bool HasPermissionCoverage(List<string> userPermissionCodes, string targetCode)
    {
        // PermA 覆盖所有权限
        if (userPermissionCodes.Contains(PermissionCodes.PermA))
            return true;

        // PermB 覆盖 BPermissionOverrides 中的权限
        if (userPermissionCodes.Contains(PermissionCodes.PermB)
            && PermissionCodes.BPermissionOverrides.Contains(targetCode))
            return true;

        // PermC 覆盖 CPermissionOverrides 中的权限
        if (userPermissionCodes.Contains(PermissionCodes.PermC)
            && PermissionCodes.CPermissionOverrides.Contains(targetCode))
            return true;

        // 精确匹配
        return userPermissionCodes.Contains(targetCode);
    }
}
