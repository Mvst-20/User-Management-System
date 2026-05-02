using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using UserManagementSystem.DTOs;
using UserManagementSystem.Extensions;
using UserManagementSystem.Models;
using UserManagementSystem.Services;

namespace UserManagementSystem.Endpoints;

public static class UserGroupEndpoints
{
    public static void MapUserGroupEndpoints(this WebApplication app)
    {
        // ============ 用户组管理 API ============

        // 获取用户组列表
        app.MapGet("/api/user-groups", async (
            [FromServices] IPermissionService permissionService,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var (groups, totalCount) = await permissionService.GetGroupsPagedAsync(page, pageSize);

            var items = groups.Select(g => new UserGroupResponse(
                g.Id, g.Name, g.Description, g.IsProtected, g.IsMemberLocked,
                g.Members.Count, g.GroupPermissions.Count
            )).ToList();

            var response = new PagedApiResponse(
                ResultCodes.Success, "获取成功",
                items.Cast<object>().ToList(), page, pageSize, totalCount
            );

            return Results.Ok(response);
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.UserGroupRead);

        // 获取用户组详情
        app.MapGet("/api/user-groups/{id}", async (
            ulong id,
            [FromServices] IPermissionService permissionService) =>
        {
            var group = await permissionService.GetGroupByIdAsync(id);
            if (group == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            var members = await permissionService.GetGroupMembersAsync(id);
            var permissions = await permissionService.GetGroupPermissionsAsync(id);

            JsonElement? extras = null;
            if (!string.IsNullOrEmpty(group.Extras))
            {
                try
                {
                    using var doc = JsonDocument.Parse(group.Extras);
                    extras = doc.RootElement.Clone();
                }
                catch { }
            }

            var response = new UserGroupDetailResponse(
                group.Id, group.Name, group.Description, group.IsProtected, group.IsMemberLocked,
                members.Select(m => new GroupMemberResponse(m.UserId, m.User.Username, m.JoinedAt)).ToList(),
                permissions.Select(p => new PermissionResponse(
                    p.Id, p.Code, p.Name, p.Category, p.IsSpecial, p.IsSystem, p.Description, p.CreatedAt
                )).ToList(),
                extras
            );

            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", response));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.UserGroupRead);

        // 创建用户组
        app.MapPost("/api/user-groups", async (
            [FromBody] CreateUserGroupRequest request,
            [FromServices] IPermissionService permissionService) =>
        {
            if (await permissionService.GetGroupByNameAsync(request.Name) != null)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupNameExists, "用户组名称已存在"));
            }

            var group = new UserGroup
            {
                Name = request.Name,
                Description = request.Description,
                IsProtected = false,
                IsMemberLocked = false
            };

            var created = await permissionService.CreateGroupAsync(group);

            return Results.Ok(new ApiResponse(ResultCodes.UserGroupCreateSuccess, "创建成功",
                new UserGroupResponse(created.Id, created.Name, created.Description, created.IsProtected, created.IsMemberLocked, 0, 0)));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.UserGroupCreate);

        // 更新用户组
        app.MapPut("/api/user-groups/{id}", async (
            ulong id,
            [FromBody] UpdateUserGroupRequest request,
            [FromServices] IPermissionService permissionService) =>
        {
            var group = await permissionService.GetGroupByIdAsync(id);
            if (group == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            if (group.IsProtected)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupProtected, "受保护的用户组不可编辑"));
            }

            if (!string.IsNullOrEmpty(request.Name) && request.Name != group.Name)
            {
                var existing = await permissionService.GetGroupByNameAsync(request.Name);
                if (existing != null)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupNameExists, "用户组名称已存在"));
                }
            }

            var updated = await permissionService.UpdateGroupAsync(id, request.Name, request.Description);
            if (updated == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.UserGroupUpdateSuccess, "更新成功",
                new UserGroupResponse(updated.Id, updated.Name, updated.Description, updated.IsProtected, updated.IsMemberLocked,
                    group.Members.Count, group.GroupPermissions.Count)));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.UserGroupUpdate);

        // 删除用户组
        app.MapDelete("/api/user-groups/{id}", async (
            ulong id,
            [FromServices] IPermissionService permissionService) =>
        {
            var group = await permissionService.GetGroupByIdAsync(id);
            if (group == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            if (group.IsProtected)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupProtected, "受保护的用户组不可删除"));
            }

            var deleted = await permissionService.DeleteGroupAsync(id);
            if (!deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.UserGroupDeleteSuccess, "删除成功"));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.UserGroupDelete);

        // ============ 成员管理 ============

        // 添加用户组成员
        app.MapPost("/api/user-groups/{id}/members", async (
            ulong id,
            [FromBody] AddGroupMemberRequest request,
            [FromServices] IPermissionService permissionService,
            [FromServices] IUserService userService) =>
        {
            var group = await permissionService.GetGroupByIdAsync(id);
            if (group == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            if (group.IsMemberLocked)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupMemberLocked, "该用户组成员锁定，不可添加"));
            }

            var user = await userService.GetByIdAsync(request.UserId);
            if (user == null || user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.GetUserFail_NotFound, "用户不存在"));
            }

            var added = await permissionService.AddMemberAsync(id, request.UserId);
            if (!added)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupMemberExists, "用户已是该组成员"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.UserGroupMemberAddSuccess, "添加成员成功"));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.UserGroupManageMembers);

        // 移除用户组成员
        app.MapDelete("/api/user-groups/{id}/members/{userId}", async (
            ulong id,
            ulong userId,
            [FromServices] IPermissionService permissionService) =>
        {
            var group = await permissionService.GetGroupByIdAsync(id);
            if (group == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            if (group.IsMemberLocked)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupMemberLocked, "该用户组成员锁定，不可移除"));
            }

            var removed = await permissionService.RemoveMemberAsync(id, userId);
            if (!removed)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupMemberNotFound, "用户不是该组成员"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.UserGroupMemberRemoveSuccess, "移除成员成功"));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.UserGroupManageMembers);

        // ============ 权限分配 ============

        // 设置用户组权限
        app.MapPut("/api/user-groups/{id}/permissions", async (
            ulong id,
            [FromBody] SetGroupPermissionsRequest request,
            [FromServices] IPermissionService permissionService) =>
        {
            var group = await permissionService.GetGroupByIdAsync(id);
            if (group == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            if (group.IsProtected)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupProtected, "受保护的用户组不可修改权限"));
            }

            // A 权限只能分配给初始用户组
            var permA = await permissionService.GetPermissionByCodeAsync(PermissionCodes.PermA);
            if (permA != null && request.PermissionIds.Contains(permA.Id))
            {
                var initialGroup = await permissionService.GetGroupByNameAsync(PermissionCodes.InitialGroupName);
                if (initialGroup == null || id != initialGroup.Id)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.PermissionAPermissionRestricted, "A权限不可分配给非初始用户组"));
                }
            }

            var success = await permissionService.SetGroupPermissionsAsync(id, request.PermissionIds);
            if (!success)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.Error, "设置权限失败，请检查权限ID是否有效"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.UserGroupPermissionSetSuccess, "设置权限成功"));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.UserGroupManagePermissions);

        // ============ 用户组 Extras ============

        // 获取用户组自定义字段
        app.MapGet("/api/user-groups/{id}/extras", async (
            ulong id,
            [FromServices] IPermissionService permissionService) =>
        {
            var group = await permissionService.GetGroupByIdAsync(id);
            if (group == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            JsonElement? extras = null;
            if (!string.IsNullOrEmpty(group.Extras))
            {
                try
                {
                    using var doc = JsonDocument.Parse(group.Extras);
                    extras = doc.RootElement.Clone();
                }
                catch { }
            }

            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功",
                new UserGroupExtrasResponse(group.Id, group.Name, extras)));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.UserGroupRead);

        // 更新用户组自定义字段
        app.MapPut("/api/user-groups/{id}/extras", async (
            ulong id,
            [FromBody] UpdateUserGroupExtrasRequest request,
            [FromServices] IPermissionService permissionService) =>
        {
            var group = await permissionService.GetGroupByIdAsync(id);
            if (group == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            if (group.IsProtected)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupProtected, "受保护的用户组不可编辑"));
            }

            var extras = request.Extras?.GetRawText();
            var updated = await permissionService.UpdateGroupExtrasAsync(id, extras);
            if (updated == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            JsonElement? result = null;
            if (!string.IsNullOrEmpty(updated.Extras))
            {
                try
                {
                    using var doc = JsonDocument.Parse(updated.Extras);
                    result = doc.RootElement.Clone();
                }
                catch { }
            }

            return Results.Ok(new ApiResponse(ResultCodes.UserGroupExtrasUpdateSuccess, "更新成功",
                new UserGroupExtrasResponse(updated.Id, updated.Name, result)));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.UserGroupUpdate);
    }
}
