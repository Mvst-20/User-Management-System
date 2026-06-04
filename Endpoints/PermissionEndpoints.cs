using Microsoft.AspNetCore.Mvc;
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
                p.Id, p.Code, p.Name, p.Category, p.IsSpecial, p.IsSystem, p.Description, p.CreatorId, p.CreatedAt
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
                p.Id, p.Code, p.Name, p.Category, p.IsSpecial, p.IsSystem, p.Description, p.CreatorId, p.CreatedAt
            )).ToList();

            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", response));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.PermissionRead);

        // 权限检查
        app.MapPost("/api/permissions/check", async (
            [FromBody] CheckPermissionRequest request,
            [FromServices] IPermissionService permissionService,
            HttpContext httpContext) =>
        {
            var currentUserId = httpContext.User.GetUserId();

            // 确定要检查的用户 ID
            var targetUserId = request.UserId ?? currentUserId;
            if (!targetUserId.HasValue)
            {
                return Results.Unauthorized();
            }

            // 检查他人权限需要 permission:read 权限
            if (request.UserId.HasValue && request.UserId.Value != currentUserId)
            {
                if (!currentUserId.HasValue || !await permissionService.HasPermissionAsync(currentUserId.Value, PermissionCodes.PermissionRead))
                {
                    return Results.Json(new ApiResponse(ResultCodes.Forbidden, "权限不足，查看他人权限需要 permission:read 权限"), statusCode: 403);
                }
            }

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
                return Results.BadRequest(new ApiResponse(ResultCodes.PermissionCodeExists, "权限代码已存在"));
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

            return Results.Ok(new ApiResponse(ResultCodes.PermissionCreateSuccess, "创建成功",
                new PermissionResponse(created.Id, created.Code, created.Name, created.Category, created.IsSpecial, created.IsSystem, created.Description, created.CreatorId, created.CreatedAt)));
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
}
