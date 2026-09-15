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
            [FromServices] IUserGroupService userGroupService,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var (groups, totalCount) = await userGroupService.GetGroupsPagedAsync(page, pageSize);

            var items = groups.Select(g => new UserGroupResponse(
                g.Id, g.Name, g.Description, g.IsProtected, g.IsMemberLocked, g.IsDefaultForNewUsers,
                g.Members.Count, g.GroupPermissions.Count, g.Level
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
            [FromServices] IUserGroupService userGroupService,
            [FromServices] ILogger<Program> logger) =>
        {
            var group = await userGroupService.GetGroupByIdAsync(id);
            if (group == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            var members = await userGroupService.GetGroupMembersAsync(id);
            var permissions = await userGroupService.GetGroupPermissionsAsync(id);

            // 成员权限级别，供前端判断能否管理该成员
            var memberLevels = await userGroupService.GetUserPrivilegeLevelsAsync(members.Select(m => m.UserId));

            var response = new UserGroupDetailResponse(
                group.Id, group.Name, group.Description, group.IsProtected, group.IsMemberLocked, group.IsDefaultForNewUsers,
                members.Select(m => new GroupMemberResponse(
                    m.UserId, m.User.Username, m.JoinedAt,
                    memberLevels.TryGetValue(m.UserId, out var memberLevel)
                        ? memberLevel
                        : PermissionCodes.GroupLevel.NoGroup)).ToList(),
                permissions.Select(p => new PermissionResponse(
                    p.Id, p.Code, p.Name, p.Category, p.IsSpecial, p.IsSystem, p.Description, p.CreatorId, p.CreatedAt
                )).ToList(),
                group.Level
            );

            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", response));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.UserGroupRead);

        // 创建用户组
        app.MapPost("/api/user-groups", async (
            [FromBody] CreateUserGroupRequest request,
            [FromServices] IUserGroupService userGroupService,
            HttpContext httpContext) =>
        {
            var actorId = httpContext.User.GetUserId();
            if (actorId is null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            if (await userGroupService.GetGroupByNameAsync(request.Name) != null)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupNameExists, "用户组名称已存在"));
            }

            // 级别校验：只能创建级别严格低于自己的权限组（数值更大），
            // 否则即可造出同级甚至更高级的组，绕过"不能管理同级用户"的规则
            var actorLevel = await userGroupService.GetUserPrivilegeLevelAsync(actorId.Value);
            var newLevel = request.Level ?? PermissionCodes.GroupLevel.Default;
            if (newLevel <= actorLevel)
            {
                return Results.Json(new ApiResponse(ResultCodes.UpdateUserFail_PrivilegeLevel,
                    $"不能创建级别高于或等于自身的权限组（自身 {actorLevel}，拟创建 {newLevel}）"), statusCode: 403);
            }

            var group = new UserGroup
            {
                Name = request.Name,
                Description = request.Description,
                IsProtected = false,
                IsMemberLocked = false,
                // 「基本用户权限」：开启后新注册用户默认加入该组
                IsDefaultForNewUsers = request.IsDefaultForNewUsers,
                Level = newLevel
            };

            var created = await userGroupService.CreateGroupAsync(group);

            return Results.Ok(new ApiResponse(ResultCodes.UserGroupCreateSuccess, "创建成功",
                new UserGroupResponse(created.Id, created.Name, created.Description, created.IsProtected,
                    created.IsMemberLocked, created.IsDefaultForNewUsers, 0, 0, created.Level)));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.UserGroupCreate);

        // 更新用户组
        app.MapPut("/api/user-groups/{id}", async (
            ulong id,
            [FromBody] UpdateUserGroupRequest request,
            [FromServices] IUserGroupService userGroupService,
            HttpContext httpContext) =>
        {
            var actorId = httpContext.User.GetUserId();
            if (actorId is null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            var group = await userGroupService.GetGroupByIdAsync(id);
            if (group == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            if (group.IsProtected)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupProtected, "受保护的用户组不可编辑"));
            }

            // 级别校验：不能编辑级别高于或等于自己的权限组；调整后的级别也必须严格低于自己
            var actorLevel = await userGroupService.GetUserPrivilegeLevelAsync(actorId.Value);
            if (group.Level <= actorLevel)
            {
                return Results.Json(new ApiResponse(ResultCodes.UpdateUserFail_PrivilegeLevel,
                    $"不能编辑级别高于或等于自身的权限组（自身 {actorLevel}，目标 {group.Level}）"), statusCode: 403);
            }

            if (request.Level.HasValue && request.Level.Value <= actorLevel)
            {
                return Results.Json(new ApiResponse(ResultCodes.UpdateUserFail_PrivilegeLevel,
                    $"不能把权限组级别调整到高于或等于自身（自身 {actorLevel}，目标 {request.Level.Value}）"), statusCode: 403);
            }

            if (!string.IsNullOrEmpty(request.Name) && request.Name != group.Name)
            {
                var existing = await userGroupService.GetGroupByNameAsync(request.Name);
                if (existing != null)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupNameExists, "用户组名称已存在"));
                }
            }

            var updated = await userGroupService.UpdateGroupAsync(
                id, request.Name, request.Description, request.Level, request.IsDefaultForNewUsers);
            if (updated == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.UserGroupUpdateSuccess, "更新成功",
                new UserGroupResponse(updated.Id, updated.Name, updated.Description, updated.IsProtected, updated.IsMemberLocked,
                    updated.IsDefaultForNewUsers, group.Members.Count, group.GroupPermissions.Count, updated.Level)));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.UserGroupUpdate);

        // 删除用户组
        app.MapDelete("/api/user-groups/{id}", async (
            ulong id,
            [FromServices] IUserGroupService userGroupService,
            HttpContext httpContext) =>
        {
            var actorId = httpContext.User.GetUserId();
            if (actorId is null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            var group = await userGroupService.GetGroupByIdAsync(id);
            if (group == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            if (group.IsProtected)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupProtected, "受保护的用户组不可删除"));
            }

            // 级别校验：不能删除级别高于或等于自己的权限组
            var actorLevel = await userGroupService.GetUserPrivilegeLevelAsync(actorId.Value);
            if (group.Level <= actorLevel)
            {
                return Results.Json(new ApiResponse(ResultCodes.UpdateUserFail_PrivilegeLevel,
                    $"不能删除级别高于或等于自身的权限组（自身 {actorLevel}，目标 {group.Level}）"), statusCode: 403);
            }

            var deleted = await userGroupService.DeleteGroupAsync(id);
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
            [FromServices] IUserGroupService userGroupService,
            [FromServices] IUserService userService,
            HttpContext httpContext) =>
        {
            var actorId = httpContext.User.GetUserId();
            if (actorId is null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            var group = await userGroupService.GetGroupByIdAsync(id);
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

            // 级别校验：不能把同级或更高级的用户（含自己）加入任何权限组，
            // 否则可绕过"不能管理同级用户"的规则
            var actorLevel = await userGroupService.GetUserPrivilegeLevelAsync(actorId.Value);
            var targetLevel = await userGroupService.GetUserPrivilegeLevelAsync(request.UserId);
            if (targetLevel <= actorLevel)
            {
                return Results.Json(new ApiResponse(ResultCodes.UpdateUserFail_PrivilegeLevel,
                    $"不能管理同级或更高级的用户（自身 {actorLevel}，目标 {targetLevel}）"), statusCode: 403);
            }

            var added = await userGroupService.AddMemberAsync(id, request.UserId);
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
            [FromServices] IUserGroupService userGroupService,
            HttpContext httpContext) =>
        {
            var actorId = httpContext.User.GetUserId();
            if (actorId is null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            // 系统初始用户 (uid=0) 不允许从任何用户组移除
            if (userId == 0)
            {
                return Results.Json(new ApiResponse(ResultCodes.UserGroupMemberLocked, "系统初始用户不可从用户组移除"), statusCode: 403);
            }

            var group = await userGroupService.GetGroupByIdAsync(id);
            if (group == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            if (group.IsMemberLocked)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupMemberLocked, "该用户组成员锁定，不可移除"));
            }

            // 级别校验：不能移除同级或更高级用户（含自己）的组成员资格
            var actorLevel = await userGroupService.GetUserPrivilegeLevelAsync(actorId.Value);
            var targetLevel = await userGroupService.GetUserPrivilegeLevelAsync(userId);
            if (targetLevel <= actorLevel)
            {
                return Results.Json(new ApiResponse(ResultCodes.UpdateUserFail_PrivilegeLevel,
                    $"不能管理同级或更高级的用户（自身 {actorLevel}，目标 {targetLevel}）"), statusCode: 403);
            }

            var removed = await userGroupService.RemoveMemberAsync(id, userId);
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
            [FromServices] IPermissionService permService,
            [FromServices] IUserGroupService userGroupService) =>
        {
            var group = await userGroupService.GetGroupByIdAsync(id);
            if (group == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));
            }

            // 成员锁定（如初始用户组）或受保护（如超级管理员组）的用户组不可修改权限
            if (group.IsMemberLocked || group.IsProtected)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupProtected, "该用户组权限不可修改"));
            }

            // A 权限只能分配给初始用户组
            var permA = await permService.GetPermissionByCodeAsync(PermissionCodes.PermA);
            if (permA != null && request.PermissionIds.Contains(permA.Id))
            {
                var initialGroup = await userGroupService.GetGroupByNameAsync(PermissionCodes.InitialGroupName);
                if (initialGroup == null || id != initialGroup.Id)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.PermissionAPermissionRestricted, "A权限不可分配给非初始用户组"));
                }
            }

            var success = await userGroupService.SetGroupPermissionsAsync(id, request.PermissionIds);
            if (!success)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.Error, "设置权限失败，请检查权限ID是否有效"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.UserGroupPermissionSetSuccess, "设置权限成功"));
        })
        .RequireAuthorization()
        .RequirePermission(PermissionCodes.UserGroupManagePermissions);

    }
}
