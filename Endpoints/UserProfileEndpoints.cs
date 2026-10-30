using Microsoft.AspNetCore.Mvc;
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

            // 权限级别校验：不能调整权限级别不低于自己的用户的权限组
            var privilegeResult = await httpContext.ValidatePrivilegeLevelAsync(userGroupService, id);
            if (privilegeResult is not null) return privilegeResult;

            // 只能分配级别严格低于自己的权限组（数值更大），否则可造出同级或更高级的用户
            var actorId = httpContext.User.GetUserId();
            if (actorId is not null)
            {
                var actorLevel = await userGroupService.GetUserPrivilegeLevelAsync(actorId.Value);
                var groupLevels = await userGroupService.GetGroupLevelsAsync(request.GroupIds);
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

            var outcome = await userGroupService.SetUserGroupsAsync(id, request.GroupIds);
            switch (outcome)
            {
                case SetUserGroupsOutcome.LockedGroupNotAssignable:
                    return Results.Json(new ApiResponse(ResultCodes.UserGroupLockedGroupNotAssignable, "受保护权限组不可分配"), statusCode: 403);

                case SetUserGroupsOutcome.InvalidGroup:
                    return Results.BadRequest(new ApiResponse(ResultCodes.Error, "设置失败，请检查权限组ID是否有效"));

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

            // 与当前状态相同视为"未变更"：界面回传当前值时不应触发管理员限制
            var statusChange = request.Status.HasValue && request.Status.Value != (int)user.Status
                ? request.Status
                : null;

            var statusResult = ValidateUserStatusForUpdate(user, statusChange, isSelf);
            if (statusResult != null) return statusResult;

            // 检查用户名唯一性（排除已软删除用户）
            if (!string.IsNullOrEmpty(request.Username) && request.Username != user.Username)
            {
                if (await userService.ActiveUsernameExistsAsync(request.Username))
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_UsernameExists, "用户名已存在"));
                }
                user.Username = request.Username;
            }

            // 只有管理员（拥有 user:update 权限操作他人时）可以修改他人邮箱
            if (!string.IsNullOrEmpty(request.Email) && request.Email != user.Email)
            {
                if (isSelf)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_Forbidden, "请使用修改邮箱接口变更邮箱"));
                }
                if (await userService.ActiveEmailExistsAsync(request.Email))
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_EmailExists, "邮箱已被使用"));
                }
                user.Email = request.Email;
                user.EmailVerifiedAt = null;
            }

            if (request.Phone != null)
            {
                if (await userService.ActivePhoneExistsAsync(request.Phone, id))
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_PhoneExists, "手机号已被使用"));
                }
                user.Phone = request.Phone;
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

            await userService.UpdateUserAsync(user);

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

            // 与当前状态相同视为"未变更"：界面回传当前值时不应触发管理员限制
            var statusChange = request.Status.HasValue && request.Status.Value != (int)user.Status
                ? request.Status
                : null;

            var statusResult = ValidateUserStatusForUpdate(user, statusChange, isSelf);
            if (statusResult != null) return statusResult;

            if (!string.IsNullOrEmpty(request.Username) && request.Username != user.Username)
            {
                if (await userService.ActiveUsernameExistsAsync(request.Username))
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_UsernameExists, "用户名已存在"));
                }
                user.Username = request.Username;
            }

            if (request.Phone != null)
            {
                if (await userService.ActivePhoneExistsAsync(request.Phone, id))
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_PhoneExists, "手机号已被使用"));
                }
                user.Phone = request.Phone;
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

            await userService.UpdateUserAsync(user);

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

            if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangePasswordFail_WrongCurrent, "当前密码错误"));
            }

            if (request.NewPassword == request.CurrentPassword)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangePasswordFail_SamePassword, "新密码不能与当前密码相同"));
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, 12);
            await userService.UpdateUserAsync(user);

            // 改密后所有设备都必须重新登录（含当前设备）
            await sessionService.RevokeAllAsync(id, SessionRevokeReasons.PasswordChanged);

            // 撤销当前 Access Token，强制重新登录
            var jti = httpContext.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
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

            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangeEmailRequestFail_WrongPassword, "密码错误"));
            }

            if (await userService.ActiveEmailExistsAsync(request.NewEmail))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangeEmailRequestFail_EmailExists, "该邮箱已被使用"));
            }

            var token = await tokenService.CreateVerificationTokenAsync(
                id, VerificationTokenType.EmailChange, request.NewEmail);

            try
            {
                await emailService.SendVerificationEmailAsync(
                    request.NewEmail, user.Username, token.Token, true, request.NewEmail);
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
