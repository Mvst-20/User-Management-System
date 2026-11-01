using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Configuration;
using UserManagementSystem.DTOs;
using UserManagementSystem.Extensions;
using UserManagementSystem.Models;
using UserManagementSystem.Services;

namespace UserManagementSystem.Endpoints;

/// <summary>
/// 用户资料管理端点：查询、更新、删除、用户组关联、密码/邮箱修改（需认证）。
/// </summary>
public static class UserProfileEndpoints
{
    /// <summary>
    /// 校验用户状态：已软删除或已封禁时返回对应的错误结果，正常时返回 null。
    /// 用于不接受状态变更的端点（改密码 / 改邮箱等）。
    /// </summary>
    private static IResult? ValidateUserStatus(User user)
    {
        if (user.Status == UserStatus.Deleted)
            return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));
        if (user.Status == UserStatus.Banned)
            return Results.Json(new ApiResponse(ResultCodes.AccountBanned, "账号已被封禁"), statusCode: 403);
        return null;
    }

    /// <summary>
    /// 校验用户状态与本次请求中「状态变更」的合法性。
    /// <para>
    /// 规则：状态仅可由管理员修改（不允许改自己，避免自助解封或跳过邮箱验证）；
    /// 已封禁用户除"管理员解封"外，其余资料修改一律拒绝。
    /// </para>
    /// 调用方需保证 <paramref name="user"/> 非空，校验通过时返回 null。
    /// </summary>
    private static IResult? ValidateUserStatusForUpdate(User user, int? newStatus, bool isSelf)
    {
        if (user.Status == UserStatus.Deleted)
        {
            return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));
        }

        if (newStatus.HasValue)
        {
            if (isSelf)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_StatusRestricted, "用户状态仅可由管理员修改"));
            }

            if ((UserStatus)newStatus.Value is not (UserStatus.Normal or UserStatus.Banned or UserStatus.Unverified))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_StatusRestricted, "不支持的用户状态"));
            }
        }

        if (user.Status == UserStatus.Banned && !newStatus.HasValue)
        {
            return Results.Json(new ApiResponse(ResultCodes.AccountBanned, "账号已被封禁"), statusCode: 403);
        }

        return null;
    }

    public static void MapUserProfileEndpoints(this WebApplication app)
    {
        // 当前登录用户的有效权限（供管理前端控制菜单与按钮显隐；仅需认证）
        app.MapGet("/api/users/me/permissions", async (
            [FromServices] IPermissionService permissionService,
            [FromServices] IUserGroupService userGroupService,
            HttpContext httpContext) =>
        {
            var userId = httpContext.User.GetUserId();
            if (userId is null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            var codes = await permissionService.GetEffectivePermissionCodesAsync(userId.Value);
            var level = await userGroupService.GetUserPrivilegeLevelAsync(userId.Value);
            var version = await permissionService.GetPermissionVersionAsync();

            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功",
                new MyPermissionsResponse(userId.Value, codes, level, version)));
        }).RequireAuthorization();

        // 当前全局权限版本号：前端轮询此值，变化后才重新拉取权限集合（仅需认证）
        app.MapGet("/api/users/me/permissions/version", async (
            [FromServices] IPermissionService permissionService) =>
        {
            var version = await permissionService.GetPermissionVersionAsync();
            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功",
                new PermissionVersionResponse(version)));
        }).RequireAuthorization();

        // 获取单个用户信息（允许查看自己，查看他人需要 user:read 权限）
        app.MapGet("/api/users/{id}", async (
            ulong id,
            [FromServices] IUserService userService) =>
        {
            var user = await userService.GetByIdAsync(id);

            if (user == null || user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.GetUserFail_NotFound, "用户不存在"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.GetUserSuccess, "获取成功", user.ToResponse()));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserRead, allowSelf: true);

        // 获取用户所属的用户组
        app.MapGet("/api/users/{id}/groups", async (
            ulong id,
            [FromServices] IUserService userService,
            [FromServices] IUserGroupService userGroupService) =>
        {
            var user = await userService.GetByIdAsync(id);
            if (user == null || user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.GetUserFail_NotFound, "用户不存在"));
            }

            var result = await userGroupService.GetUserGroupSummariesAsync(id);

            return Results.Ok(new ApiResponse(ResultCodes.GetUserSuccess, "获取成功", result));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserRead, allowSelf: true);

        // 批量设置用户所属用户组（替换现存所有组，uid=0 除外）
        app.MapPut("/api/users/{id}/groups", async (
            ulong id,
            [FromBody] SetUserGroupsRequest request,
            [FromServices] IUserService userService,
            [FromServices] IUserGroupService userGroupService,
            HttpContext httpContext) =>
        {
            var user = await userService.GetByIdAsync(id);
            if (user == null || user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.GetUserFail_NotFound, "用户不存在"));
            }

            if (id == 0)
            {
                return Results.Json(new ApiResponse(ResultCodes.UserGroupMemberLocked, "系统初始用户的用户组不可编辑"), statusCode: 403);
            }

            // 不能修改自己的权限组：避免管理员误操作自我降级、失去管理能力
            var selfId = httpContext.User.GetUserId();
            if (selfId.HasValue && selfId.Value == id)
            {
                return Results.Json(new ApiResponse(ResultCodes.UpdateUserFail_Forbidden, "不能修改自己的权限组"), statusCode: 403);
            }

            // 权限组设置：允许“纯移除成员锁定组”的修复路径（只降权、不提权）
            var actorId = httpContext.User.GetUserId();
            if (actorId is null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            var currentGroupIds = await userGroupService.GetUserGroupIdsAsync(id);
            var requestedGroupIds = request.GroupIds?.Distinct().ToList() ?? new List<ulong>();

            // 读取涉及到的组（组不存在时返回 1100，而不是通用 code=1）
            var groups = new Dictionary<ulong, UserGroup?>();
            foreach (var groupId in currentGroupIds.Concat(requestedGroupIds).Distinct())
            {
                groups[groupId] = await userGroupService.GetGroupByIdAsync(groupId);
            }

            var missingGroupIds = requestedGroupIds
                .Where(groupId => groups.GetValueOrDefault(groupId) is null)
                .ToList();
            if (missingGroupIds.Count > 0)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupNotFound,
                    $"用户组不存在：{string.Join("/", missingGroupIds)}"));
            }

            // 成员锁定的受保护组（初始用户组）不可“新分配”；先于级别校验，保证 1125 可达。
            var lockedNewGroupIds = requestedGroupIds
                .Where(groupId => !currentGroupIds.Contains(groupId) && groups[groupId]!.IsMemberLocked)
                .ToList();
            if (lockedNewGroupIds.Count > 0)
            {
                return Results.Json(new ApiResponse(ResultCodes.UserGroupLockedGroupNotAssignable, "受保护权限组不可分配"),
                    statusCode: 403);
            }

            // 级别校验：目标级别不低于自己时，仅允许“移除成员锁定组”的纯降权操作。
            var actorLevel = await userGroupService.GetUserPrivilegeLevelAsync(actorId.Value);
            var targetLevel = await userGroupService.GetUserPrivilegeLevelAsync(id);
            if (targetLevel <= actorLevel)
            {
                var removedGroupIds = currentGroupIds.Except(requestedGroupIds).ToList();
                var removesLockedGroup = removedGroupIds
                    .Any(groupId => groups.GetValueOrDefault(groupId)?.IsMemberLocked == true);
                var pureRemoval = requestedGroupIds.All(currentGroupIds.Contains) && removesLockedGroup;

                if (!pureRemoval)
                {
                    return Results.Json(new ApiResponse(ResultCodes.UpdateUserFail_PrivilegeLevel,
                        $"目标用户的权限级别不低于你（目标 {targetLevel}，自身 {actorLevel}），无法操作"),
                        statusCode: 403);
                }
            }

            // 新增的组必须严格低于操作者级别（数值更大）
            var newGroupIds = requestedGroupIds.Where(groupId => !currentGroupIds.Contains(groupId)).ToList();
            if (newGroupIds.Count > 0)
            {
                var groupLevels = await userGroupService.GetGroupLevelsAsync(newGroupIds);
                var illegal = groupLevels
                    .Where(pair => !PrivilegeLevelExtensions.CanManageGroup(actorLevel, pair.Value))
                    .Select(pair => pair.Key)
                    .ToList();

                if (illegal.Count > 0)
                {
                    return Results.Json(new ApiResponse(ResultCodes.UpdateUserFail_PrivilegeLevel,
                        $"不能分配级别高于或等于自身的权限组（自身 {actorLevel}，不可分配 {string.Join("/", illegal)}）"),
                        statusCode: 403);
                }
            }

            var outcome = await userGroupService.SetUserGroupsAsync(id, requestedGroupIds);
            switch (outcome)
            {
                case SetUserGroupsOutcome.LockedGroupNotAssignable:
                    return Results.Json(new ApiResponse(ResultCodes.UserGroupLockedGroupNotAssignable, "受保护权限组不可分配"), statusCode: 403);

                case SetUserGroupsOutcome.InvalidGroup:
                    return Results.BadRequest(new ApiResponse(ResultCodes.UserGroupNotFound, "用户组不存在"));

                default:
                    return Results.Ok(new ApiResponse(ResultCodes.Success, "设置成功"));
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserGroupManageMembers);

        // 完全更新用户信息（允许更新自己，更新他人需要 user:update 权限）
        app.MapPut("/api/users/{id}", async (
            ulong id,
            [FromBody] UpdateUserRequest request,
            [FromServices] IUserService userService,
            [FromServices] ISessionService sessionService,
            [FromServices] IUserGroupService userGroupService,
            HttpContext httpContext) =>
        {
            // 保护系统初始用户 (uid=0) 的核心信息不被修改
            if (id == 0)
            {
                return Results.Json(new ApiResponse(ResultCodes.UpdateUserFail_Forbidden, "系统初始用户信息不可修改"), statusCode: 403);
            }

            // 权限级别校验：不能修改权限级别不低于自己的用户
            var privilegeResult = await httpContext.ValidatePrivilegeLevelAsync(userGroupService, id);
            if (privilegeResult is not null) return privilegeResult;

            var user = await userService.GetByIdAsync(id);
            if (user == null)
                return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));

            var isSelf = httpContext.User.IsOwnResource(id);
            var wasDeleted = user.Status == UserStatus.Deleted;

            // 与当前状态相同视为"未变更"：界面回传当前值时不应触发管理员限制
            var statusChange = request.Status.HasValue && request.Status.Value != (int)user.Status
                ? request.Status
                : null;

            var statusResult = ValidateUserStatusForUpdate(user, statusChange, isSelf);
            if (statusResult != null) return statusResult;

            // 用户名：统一校验（长度/控制字符）+ 唯一性检查（排除已软删除用户）
            if (!string.IsNullOrEmpty(request.Username) && request.Username != user.Username)
            {
                var usernameError = UserValidation.ValidateUsername(request.Username, out var username);
                if (usernameError != null)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, usernameError));
                }

                if (await userService.ActiveUsernameExistsAsync(username))
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_UsernameExists, "用户名已存在"));
                }
                user.Username = username;
            }

            // 只有管理员（拥有 user:update 权限操作他人时）可以修改他人邮箱
            if (!string.IsNullOrEmpty(request.Email) && request.Email != user.Email)
            {
                if (isSelf)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_Forbidden, "请使用修改邮箱接口变更邮箱"));
                }

                var emailError = UserValidation.ValidateEmail(request.Email, out var email);
                if (emailError != null)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, emailError));
                }

                if (await userService.ActiveEmailExistsAsync(email))
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_EmailExists, "邮箱已被使用"));
                }
                user.Email = email;
                user.EmailVerifiedAt = null;
            }

            if (request.Phone != null)
            {
                var phoneError = UserValidation.ValidatePhone(request.Phone, out var phone);
                if (phoneError != null)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, phoneError));
                }

                if (phone != null && await userService.ActivePhoneExistsAsync(phone, id))
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_PhoneExists, "手机号已被使用"));
                }
                user.Phone = phone;
            }

            if (request.Extras.HasValue)
            {
                user.Extras = request.Extras.Value.GetRawText();
            }

            if (statusChange.HasValue)
            {
                var newStatus = (UserStatus)statusChange.Value;
                // 记录/清除注销时间（供"注销用户"逐日统计），与状态迁移保持一致
                if (newStatus == UserStatus.Deleted && user.Status != UserStatus.Deleted)
                {
                    user.DeletedAt = DateTime.UtcNow;
                }
                else if (newStatus != UserStatus.Deleted && user.Status == UserStatus.Deleted)
                {
                    user.DeletedAt = null;
                }

                user.Status = newStatus;
            }

            // 软删除用户恢复时，若用户名/邮箱已被新用户占用，返回明确业务码而非数据库唯一键 500
            if (wasDeleted && statusChange is { } restoreStatus && (UserStatus)restoreStatus != UserStatus.Deleted)
            {
                if (await userService.ActiveUsernameExistsAsync(user.Username))
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_UsernameExists, "用户名已被其他用户使用，无法恢复"));
                }
                if (await userService.ActiveEmailExistsAsync(user.Email))
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_EmailExists, "邮箱已被其他用户使用，无法恢复"));
                }
            }

            try
            {
                await userService.UpdateUserAsync(user);
            }
            catch (DbUpdateException ex) when (ex.InnerException is MySqlConnector.MySqlException mysqlEx
                                               && mysqlEx.ErrorCode == MySqlConnector.MySqlErrorCode.DuplicateKeyEntry)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_UsernameExists, "用户名、邮箱或手机号已被使用"));
            }

            // 封禁/注销时立刻踢掉所有设备上的会话，避免已登录用户继续访问
            await RevokeSessionsForStatusAsync(sessionService, id, statusChange);

            return Results.Ok(new ApiResponse(ResultCodes.UpdateUserSuccess, "更新成功", user.ToResponse()));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserUpdate, allowSelf: true);

        // 部分更新用户信息
        app.MapPatch("/api/users/{id}", async (
            ulong id,
            [FromBody] PatchUserRequest request,
            [FromServices] IUserService userService,
            [FromServices] ISessionService sessionService,
            [FromServices] IUserGroupService userGroupService,
            HttpContext httpContext) =>
        {
            // 保护系统初始用户 (uid=0) 的核心信息不被修改
            if (id == 0)
            {
                return Results.Json(new ApiResponse(ResultCodes.UpdateUserFail_Forbidden, "系统初始用户信息不可修改"), statusCode: 403);
            }

            // 权限级别校验：不能修改权限级别不低于自己的用户
            var privilegeResult = await httpContext.ValidatePrivilegeLevelAsync(userGroupService, id);
            if (privilegeResult is not null) return privilegeResult;

            var user = await userService.GetByIdAsync(id);
            if (user == null)
                return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));

            var isSelf = httpContext.User.IsOwnResource(id);
            var wasDeleted = user.Status == UserStatus.Deleted;

            // 与当前状态相同视为"未变更"：界面回传当前值时不应触发管理员限制
            var statusChange = request.Status.HasValue && request.Status.Value != (int)user.Status
                ? request.Status
                : null;

            var statusResult = ValidateUserStatusForUpdate(user, statusChange, isSelf);
            if (statusResult != null) return statusResult;

            if (!string.IsNullOrEmpty(request.Username) && request.Username != user.Username)
            {
                var usernameError = UserValidation.ValidateUsername(request.Username, out var username);
                if (usernameError != null)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, usernameError));
                }

                if (await userService.ActiveUsernameExistsAsync(username))
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_UsernameExists, "用户名已存在"));
                }
                user.Username = username;
            }

            if (request.Phone != null)
            {
                var phoneError = UserValidation.ValidatePhone(request.Phone, out var phone);
                if (phoneError != null)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, phoneError));
                }

                if (phone != null && await userService.ActivePhoneExistsAsync(phone, id))
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_PhoneExists, "手机号已被使用"));
                }
                user.Phone = phone;
            }

            if (request.Extras.HasValue)
            {
                user.Extras = request.Extras.Value.GetRawText();
            }

            if (statusChange.HasValue)
            {
                var newStatus = (UserStatus)statusChange.Value;
                // 记录/清除注销时间（供"注销用户"逐日统计），与状态迁移保持一致
                if (newStatus == UserStatus.Deleted && user.Status != UserStatus.Deleted)
                {
                    user.DeletedAt = DateTime.UtcNow;
                }
                else if (newStatus != UserStatus.Deleted && user.Status == UserStatus.Deleted)
                {
                    user.DeletedAt = null;
                }

                user.Status = newStatus;
            }

            // 软删除用户恢复时，若用户名/邮箱已被新用户占用，返回明确业务码而非数据库唯一键 500
            if (wasDeleted && statusChange is { } restoreStatus && (UserStatus)restoreStatus != UserStatus.Deleted)
            {
                if (await userService.ActiveUsernameExistsAsync(user.Username))
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_UsernameExists, "用户名已被其他用户使用，无法恢复"));
                }
                if (await userService.ActiveEmailExistsAsync(user.Email))
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_EmailExists, "邮箱已被其他用户使用，无法恢复"));
                }
            }

            try
            {
                await userService.UpdateUserAsync(user);
            }
            catch (DbUpdateException ex) when (ex.InnerException is MySqlConnector.MySqlException mysqlEx
                                               && mysqlEx.ErrorCode == MySqlConnector.MySqlErrorCode.DuplicateKeyEntry)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_UsernameExists, "用户名、邮箱或手机号已被使用"));
            }

            // 封禁/注销时立刻踢掉所有设备上的会话
            await RevokeSessionsForStatusAsync(sessionService, id, statusChange);

            return Results.Ok(new ApiResponse(ResultCodes.UpdateUserSuccess, "更新成功", user.ToResponse()));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserUpdate, allowSelf: true);

        // 软删除用户（需要 user:delete 权限）
        app.MapDelete("/api/users/{id}", async (
            ulong id,
            [FromServices] IUserService userService,
            [FromServices] ISessionService sessionService,
            [FromServices] IUserGroupService userGroupService,
            HttpContext httpContext) =>
        {
            // 保护系统初始用户 (uid=0) 不被删除
            if (id == 0)
            {
                return Results.Json(new ApiResponse(ResultCodes.DeleteUserFail_Forbidden, "系统初始用户不可删除"), statusCode: 403);
            }

            // 权限级别校验：不能删除权限级别不低于自己的用户
            var privilegeResult = await httpContext.ValidatePrivilegeLevelAsync(userGroupService, id);
            if (privilegeResult is not null) return privilegeResult;

            // 防止用户删除自己
            var currentUserId = httpContext.User.GetUserId();
            if (currentUserId.HasValue && currentUserId.Value == id)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.DeleteUserFail_SelfDelete, "不能删除自己的账号"));
            }

            var result = await userService.DeleteUserAsync(id);

            if (!result)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.DeleteUserFail_NotFound, "用户不存在"));
            }

            // 用户被软删除后其会话必须立即失效
            await sessionService.RevokeAllAsync(id, SessionRevokeReasons.UserDeleted);

            return Results.Ok(new ApiResponse(ResultCodes.DeleteUserSuccess, "用户已删除"));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserDelete);

        // 分页查询用户列表（需要 user:list 权限）
        app.MapGet("/api/users", async (
            [FromServices] IUserService userService,
            [FromServices] IUserGroupService userGroupService,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] int? status = null,
            [FromQuery] string? search = null) =>
        {
            // pageSize 上限保护
            if (pageSize < 1) pageSize = 1;
            if (pageSize > 100) pageSize = 100;
            if (page < 1) page = 1;

            var (users, totalCount) = await userService.GetUsersAsync(page, pageSize, status, search);

            // 批量填充权限级别，供前端判断能否操作该用户
            var levels = await userGroupService.GetUserPrivilegeLevelsAsync(users.Select(u => u.Id));

            var response = new PagedApiResponse(
                ResultCodes.GetUsersSuccess, "获取成功",
                users.Select(u => (object)(u.ToResponse() with
                {
                    PrivilegeLevel = levels.GetValueOrDefault(u.Id, PermissionCodes.GroupLevel.NoGroup)
                })).ToList(),
                page, pageSize, totalCount
            );

            return Results.Ok(response);
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserList);

        // 修改密码（仅限本人操作）
        app.MapPost("/api/users/{id}/change-password", async (
            ulong id,
            [FromBody] ChangePasswordRequest request,
            [FromServices] IUserService userService,
            [FromServices] ISessionService sessionService,
            [FromServices] ITokenBlacklistService tokenBlacklistService,
            [FromServices] AppConfiguration appConfig,
            HttpContext httpContext) =>
        {
            // 密码修改仅限本人，不允许管理员代操作
            if (!httpContext.User.IsOwnResource(id))
            {
                return Results.Json(new ApiResponse(ResultCodes.Forbidden, "禁止访问"), statusCode: 403);
            }

            var user = await userService.GetByIdAsync(id);
            if (user == null)
                return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));

            var statusResult = ValidateUserStatus(user);
            if (statusResult != null) return statusResult;

            if (!PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangePasswordFail_WrongCurrent, "当前密码错误"));
            }

            // 新密码长度/格式校验：与注册、管理员重置保持同一规则（8-128）
            var passwordError = UserValidation.ValidatePassword(request.NewPassword);
            if (passwordError != null)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, passwordError));
            }

            if (request.NewPassword == request.CurrentPassword)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangePasswordFail_SamePassword, "新密码不能与当前密码相同"));
            }

            user.PasswordHash = PasswordHasher.Hash(request.NewPassword!);
            await userService.UpdateUserAsync(user);

            // 改密后所有设备都必须重新登录（含当前设备）
            await sessionService.RevokeAllAsync(id, SessionRevokeReasons.PasswordChanged);

            // 撤销当前 Access Token，强制重新登录
            var jti = httpContext.User.GetJti();
            if (jti != null)
            {
                await tokenBlacklistService.BlacklistAsync(jti, DateTime.UtcNow.AddMinutes(appConfig.Jwt.ExpiryMinutes));
            }

            return Results.Ok(new ApiResponse(ResultCodes.ChangePasswordSuccess, "密码修改成功，请重新登录"));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserChangePassword);

        // 申请修改邮箱（仅限本人操作）
        app.MapPost("/api/users/{id}/change-email", async (
            ulong id,
            [FromBody] ChangeEmailRequest request,
            [FromServices] IUserService userService,
            [FromServices] ITokenService tokenService,
            [FromServices] IEmailService emailService,
            HttpContext httpContext,
            [FromServices] ILogger<Program> logger) =>
        {
            // 邮箱修改仅限本人
            if (!httpContext.User.IsOwnResource(id))
            {
                return Results.Json(new ApiResponse(ResultCodes.Forbidden, "禁止访问"), statusCode: 403);
            }

            var user = await userService.GetByIdAsync(id);
            if (user == null)
                return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));

            var statusResult = ValidateUserStatus(user);
            if (statusResult != null) return statusResult;

            if (request.NewEmail == user.Email)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangeEmailRequestFail_SameEmail, "新邮箱与旧邮箱相同"));
            }

            // 新邮箱格式/长度校验，避免把非法值写入待验证 token 并触发数据库异常
            var emailError = UserValidation.ValidateEmail(request.NewEmail, out var newEmail);
            if (emailError != null)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, emailError));
            }

            if (!PasswordHasher.Verify(request.Password, user.PasswordHash))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangeEmailRequestFail_WrongPassword, "密码错误"));
            }

            if (await userService.ActiveEmailExistsAsync(newEmail))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangeEmailRequestFail_EmailExists, "该邮箱已被使用"));
            }

            var token = await tokenService.CreateVerificationTokenAsync(
                id, VerificationTokenType.EmailChange, newEmail);

            try
            {
                await emailService.SendVerificationEmailAsync(
                    newEmail, user.Username, token.Token, true, newEmail);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send email change verification email");
                return Results.BadRequest(new ApiResponse(ResultCodes.EmailSendFail, "邮件发送失败"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.ChangeEmailRequestSuccess, "验证邮件已发送到新邮箱，请查收"));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserChangeEmail);

        // 状态变更为「封禁 / 注销」时撤销该用户全部会话：
        // 会话撤销写入 last_jti 黑名单，所有实例立即生效，避免旧 token 在有效期内继续访问。
        static async Task RevokeSessionsForStatusAsync(ISessionService sessionService, ulong userId, int? statusChange)
        {
            if (!statusChange.HasValue)
            {
                return;
            }

            var reason = (UserStatus)statusChange.Value switch
            {
                UserStatus.Banned => SessionRevokeReasons.Banned,
                UserStatus.Deleted => SessionRevokeReasons.UserDeleted,
                _ => null
            };

            if (reason != null)
            {
                await sessionService.RevokeAllAsync(userId, reason);
            }
        }
    }
}
