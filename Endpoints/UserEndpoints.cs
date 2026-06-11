using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using UserManagementSystem.Configuration;
using UserManagementSystem.DTOs;
using UserManagementSystem.Extensions;
using UserManagementSystem.Models;
using UserManagementSystem.Services;

namespace UserManagementSystem.Endpoints;

public static class UserEndpoints
{
    private static readonly object _rateLimitLock = new object();

    /// <summary>
    /// 原子化的速率限制检查：检查限制并在允许时递增计数
    /// </summary>
    /// <returns>如果超过限制返回 true，否则返回 false</returns>
    private static bool IsRateLimited(IMemoryCache cache, string key, int maxAttempts, TimeSpan window)
    {
        lock (_rateLimitLock)
        {
            var count = 0;
            if (cache.TryGetValue(key, out int currentCount))
            {
                count = currentCount;
            }

            if (count >= maxAttempts)
            {
                return true;
            }

            cache.Set(key, count + 1, window);
            return false;
        }
    }

    /// <summary>
    /// 验证用户状态：检查用户是否存在、是否被软删除、是否被封禁
    /// </summary>
    /// <returns>如果用户状态异常则返回对应的 IResult，否则返回 null 表示用户状态正常</returns>
    private static IResult? ValidateUserStatus(User? user)
    {
        if (user == null || user.Status == UserStatus.Deleted)
            return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));
        if (user.Status == UserStatus.Banned)
            return Results.Json(new ApiResponse(ResultCodes.AccountBanned, "账号已被封禁"), statusCode: 403);
        return null;
    }

    public static void MapUserEndpoints(this WebApplication app)
    {
        // ============ 公开端点（不需要权限检查）============

        // 用户注册
        app.MapPost("/api/users/register", async (
            [FromBody] RegisterRequest request,
            [FromServices] IUserService userService,
            [FromServices] ITokenService tokenService,
            [FromServices] IEmailService emailService,
            [FromServices] IPermissionService permissionService,
            [FromServices] ISystemSettingService systemSettingService,
            [FromServices] IMemoryCache cache,
            [FromServices] ILogger<Program> logger) =>
        {
            // 检查是否允许注册
            if (!await systemSettingService.GetBoolSettingAsync("allow_register", true))
            {
                return Results.Json(new ApiResponse(ResultCodes.Forbidden, "系统暂未开放注册"), statusCode: 403);
            }

            // 注册速率限制（同一邮箱每分钟最多5次）
            var rateLimitKey = $"register_rate_{request.Email}";
            if (IsRateLimited(cache, rateLimitKey, 5, TimeSpan.FromMinutes(1)))
            {
                return Results.Json(new ApiResponse(ResultCodes.TooManyRequests, "请求过于频繁，请稍后再试"), statusCode: 429);
            }

            // 检查用户名是否已存在（排除已软删除用户）
            if (await userService.ActiveUsernameExistsAsync(request.Username))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.RegisterFail_UsernameExists, "用户名已存在"));
            }

            // 检查邮箱是否已存在（排除已软删除用户）
            if (await userService.ActiveEmailExistsAsync(request.Email))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.RegisterFail_EmailExists, "邮箱已被注册"));
            }

            // 创建用户
            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12),
                Status = UserStatus.Unverified,
                CreatedAt = DateTime.UtcNow,
                Extras = request.Extras?.GetRawText()
            };

            await userService.CreateUserAsync(user);

            // 自动加入"普通用户"组
            var normalGroup = await permissionService.GetGroupByNameAsync(PermissionCodes.NormalGroupName);
            if (normalGroup != null)
            {
                await permissionService.AddMemberAsync(normalGroup.Id, user.Id);
            }

            // 生成验证Token
            var token = await tokenService.CreateVerificationTokenAsync(user.Id, VerificationTokenType.Registration);

            // 发送验证邮件
            bool emailSent = true;
            try
            {
                await emailService.SendVerificationEmailAsync(user.Email, user.Username, token.Token);
            }
            catch (Exception ex)
            {
                emailSent = false;
                logger.LogError(ex, "Failed to send verification email to {Email}", user.Email);
            }

            var message = emailSent
                ? "注册成功，请查收验证邮件"
                : "注册成功，但验证邮件发送失败，请稍后重新发送验证邮件";

            return Results.Ok(new ApiResponse(ResultCodes.RegisterSuccess, message,
                new RegisterDataResponse(user.Email, emailSent)));
        });

        // 用户登录
        app.MapPost("/api/users/login", async (
            [FromBody] LoginRequest request,
            [FromServices] IUserService userService,
            [FromServices] IJwtService jwtService,
            [FromServices] IRefreshTokenService refreshTokenService,
            [FromServices] IOnlineUserService onlineUserService,
            [FromServices] ISystemSettingService systemSettingService,
            [FromServices] IMemoryCache cache,
            [FromServices] AppConfiguration appConfig,
            HttpContext context) =>
        {
            // 检查是否允许登录
            if (!await systemSettingService.GetBoolSettingAsync("allow_login", true))
            {
                return Results.Json(new ApiResponse(ResultCodes.Forbidden, "系统暂未开放登录"), statusCode: 403);
            }

            // 登录暴力破解防护：基于 IP + 登录标识限流
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var loginKey = request.Login ?? string.Empty;
            var ipKey = $"login_ip_{clientIp}";
            var accountKey = $"login_account_{loginKey}";

            // IP 限流：同一 IP 5 分钟内最多 20 次失败
            if (IsRateLimited(cache, ipKey, 20, TimeSpan.FromMinutes(5)))
                return Results.Json(new ApiResponse(ResultCodes.LoginFail_RateLimited, "登录尝试过于频繁，请稍后再试"), statusCode: 429);

            // 账号限流：同一账号 5 分钟内最多 5 次失败
            if (IsRateLimited(cache, accountKey, 5, TimeSpan.FromMinutes(5)))
                return Results.Json(new ApiResponse(ResultCodes.LoginFail_RateLimited, "登录尝试过于频繁，请稍后再试"), statusCode: 429);

            var user = await userService.ValidateCredentialsAsync(request.Login ?? string.Empty, request.Password);

            if (user == null)
            {
                // 登录失败，递增计数已在 IsRateLimited 中完成
                return Results.BadRequest(new ApiResponse(ResultCodes.LoginFail_UserNotFound, "用户名或密码错误"));
            }

            if (user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.LoginFail_AccountDeleted, "账号不存在或已注销"));
            }

            if (user.Status == UserStatus.Banned)
            {
                return Results.Json(new ApiResponse(ResultCodes.LoginFail_AccountBanned, "账号已被封禁"), statusCode: 403);
            }

            if (user.Status == UserStatus.Unverified)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.LoginFail_AccountUnverified, "账号未验证，请先验证邮箱"));
            }

            // 更新最后登录信息
            user.LastLoginIp = context.Connection.RemoteIpAddress?.ToString();
            user.LastLoginAt = DateTime.UtcNow;
            await userService.UpdateUserAsync(user);

            // 标记用户在线
            onlineUserService.MarkUserOnline(user.Id.ToString());

            // 生成 Access Token 和 Refresh Token
            var jwtToken = jwtService.GenerateToken(user);
            var refreshToken = await refreshTokenService.GenerateAsync(user.Id);

            var loginData = new LoginDataResponse(jwtToken, "Bearer", appConfig.Jwt.ExpiryMinutes,
                user.ToResponse(), refreshToken, 2592000); // 30 days

            return Results.Ok(new ApiResponse(ResultCodes.LoginSuccess, "登录成功", loginData));
        });

        // 刷新 Access Token（使用 Refresh Token，无需重新登录）
        app.MapPost("/api/users/refresh", async (
            [FromBody] RefreshTokenRequest request,
            [FromServices] IRefreshTokenService refreshTokenService,
            [FromServices] IJwtService jwtService,
            [FromServices] ISystemSettingService systemSettingService,
            [FromServices] AppConfiguration appConfig) =>
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "Refresh Token 不能为空"));
            }

            // 检查是否允许登录
            if (!await systemSettingService.GetBoolSettingAsync("allow_login", true))
            {
                return Results.Json(new ApiResponse(ResultCodes.Forbidden, "系统暂未开放登录"), statusCode: 403);
            }

            var user = await refreshTokenService.ValidateAsync(request.RefreshToken);
            if (user == null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "Refresh Token 无效或已过期"), statusCode: 401);
            }

            var jwtToken = jwtService.GenerateToken(user);
            var newRefreshToken = await refreshTokenService.GenerateAsync(user.Id);

            var data = new LoginDataResponse(jwtToken, "Bearer", appConfig.Jwt.ExpiryMinutes,
                user.ToResponse(), newRefreshToken, 2592000);

            return Results.Ok(new ApiResponse(ResultCodes.LoginSuccess, "Token 已刷新", data));
        });

        // 验证邮箱
        app.MapGet("/api/users/verify-email", async (
            [FromQuery] string token,
            [FromServices] IUserService userService,
            [FromServices] ITokenService tokenService) =>
        {
            if (string.IsNullOrEmpty(token))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.EmailVerifyFail_InvalidToken, "无效的验证链接"));
            }

            var verificationToken = await tokenService.GetVerificationTokenAsync(token);

            if (verificationToken == null)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.EmailVerifyFail_InvalidToken, "无效的验证链接"));
            }

            if (verificationToken.ExpiresAt < DateTime.UtcNow)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.EmailVerifyFail_TokenExpired, "验证链接已过期"));
            }

            if (verificationToken.Type != VerificationTokenType.Registration)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.EmailVerifyFail_InvalidType, "无效的验证类型"));
            }

            // 更新用户状态
            var user = verificationToken.User;
            user.Status = UserStatus.Normal;
            user.EmailVerifiedAt = DateTime.UtcNow;
            await userService.UpdateUserAsync(user);

            // 删除Token
            await tokenService.DeleteVerificationTokenAsync(verificationToken.Id);

            return Results.Ok(new ApiResponse(ResultCodes.EmailVerifySuccess, "邮箱验证成功"));
        });

        // 重新发送验证邮件
        app.MapPost("/api/users/resend-verification", async (
            [FromBody] ResendVerificationRequest request,
            [FromServices] IUserService userService,
            [FromServices] ITokenService tokenService,
            [FromServices] IEmailService emailService,
            [FromServices] IMemoryCache cache,
            [FromServices] ILogger<Program> logger) =>
        {
            var cacheKey = $"resend_verification_{request.Email}";
            if (cache.TryGetValue(cacheKey, out _))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ResendVerificationFail_RateLimited, "请稍后再试"));
            }

            var user = await userService.GetByEmailAsync(request.Email);

            if (user == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.ResendVerificationFail_UserNotFound, "用户不存在"));
            }

            if (user.Status != UserStatus.Unverified)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ResendVerificationFail_AlreadyVerified, "该用户无需验证"));
            }

            var newToken = await tokenService.CreateVerificationTokenAsync(user.Id, VerificationTokenType.Registration);
            try
            {
                await emailService.SendVerificationEmailAsync(user.Email, user.Username, newToken.Token);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send verification email");
                return Results.BadRequest(new ApiResponse(ResultCodes.EmailSendFail, "邮件发送失败"));
            }

            cache.Set(cacheKey, true, TimeSpan.FromMinutes(1));

            return Results.Ok(new ApiResponse(ResultCodes.ResendVerificationSuccess, "验证邮件已发送"));
        });

        // ============ 需要认证和权限的端点 ============

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
            [FromServices] IPermissionService permissionService) =>
        {
            var user = await userService.GetByIdAsync(id);
            if (user == null || user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.GetUserFail_NotFound, "用户不存在"));
            }

            var groupMembers = await permissionService.GetUserGroupsAsync(id);
            var result = groupMembers.Select(gm => new UserGroupResponse(
                gm.Group.Id, gm.Group.Name, gm.Group.Description, gm.Group.IsProtected, gm.Group.IsMemberLocked,
                gm.Group.Members.Count, gm.Group.GroupPermissions.Count
            )).ToList();

            return Results.Ok(new ApiResponse(ResultCodes.GetUserSuccess, "获取成功", result));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserRead, allowSelf: true);

        // 批量设置用户所属用户组（替换现存所有组，uid=0 除外）
        app.MapPut("/api/users/{id}/groups", async (
            ulong id,
            [FromBody] SetUserGroupsRequest request,
            [FromServices] IUserService userService,
            [FromServices] IPermissionService permissionService) =>
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

            var success = await permissionService.SetUserGroupsAsync(id, request.GroupIds);
            if (!success)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.Error, "设置失败，请检查用户组ID是否有效"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.Success, "设置成功"));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserGroupManageMembers);

        // 完全更新用户信息（允许更新自己，更新他人需要 user:update 权限）
        app.MapPut("/api/users/{id}", async (
            ulong id,
            [FromBody] UpdateUserRequest request,
            [FromServices] IUserService userService,
            HttpContext httpContext) =>
        {
            // 保护系统初始用户 (uid=0) 的核心信息不被修改
            if (id == 0)
            {
                return Results.Json(new ApiResponse(ResultCodes.UpdateUserFail_Forbidden, "系统初始用户信息不可修改"), statusCode: 403);
            }

            var user = await userService.GetByIdAsync(id);
            var statusResult = ValidateUserStatus(user);
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
                if (httpContext.User.IsOwnResource(id))
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

            await userService.UpdateUserAsync(user);

            return Results.Ok(new ApiResponse(ResultCodes.UpdateUserSuccess, "更新成功", user.ToResponse()));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserUpdate, allowSelf: true);

        // 部分更新用户信息
        app.MapPatch("/api/users/{id}", async (
            ulong id,
            [FromBody] PatchUserRequest request,
            [FromServices] IUserService userService) =>
        {
            // 保护系统初始用户 (uid=0) 的核心信息不被修改
            if (id == 0)
            {
                return Results.Json(new ApiResponse(ResultCodes.UpdateUserFail_Forbidden, "系统初始用户信息不可修改"), statusCode: 403);
            }

            var user = await userService.GetByIdAsync(id);
            var statusResult = ValidateUserStatus(user);
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

            await userService.UpdateUserAsync(user);

            return Results.Ok(new ApiResponse(ResultCodes.UpdateUserSuccess, "更新成功", user.ToResponse()));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserUpdate, allowSelf: true);

        // 软删除用户（需要 user:delete 权限）
        app.MapDelete("/api/users/{id}", async (
            ulong id,
            [FromServices] IUserService userService,
            [FromServices] IOnlineUserService onlineUserService,
            HttpContext httpContext) =>
        {
            // 保护系统初始用户 (uid=0) 不被删除
            if (id == 0)
            {
                return Results.Json(new ApiResponse(ResultCodes.DeleteUserFail_Forbidden, "系统初始用户不可删除"), statusCode: 403);
            }

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

            onlineUserService.MarkUserOffline(id.ToString());

            return Results.Ok(new ApiResponse(ResultCodes.DeleteUserSuccess, "用户已删除"));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserDelete);

        // 分页查询用户列表（需要 user:list 权限）
        app.MapGet("/api/users", async (
            [FromServices] IUserService userService,
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

            var response = new PagedApiResponse(
                ResultCodes.GetUsersSuccess, "获取成功",
                users.Select(u => (object)u.ToResponse()).ToList(),
                page, pageSize, totalCount
            );

            return Results.Ok(response);
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserList);

        // 修改密码（仅限本人操作）
        app.MapPost("/api/users/{id}/change-password", async (
            ulong id,
            [FromBody] ChangePasswordRequest request,
            [FromServices] IUserService userService,
            [FromServices] IOnlineUserService onlineUserService,
            [FromServices] ITokenBlacklistService tokenBlacklistService,
            [FromServices] IRefreshTokenService refreshTokenService,
            [FromServices] AppConfiguration appConfig,
            HttpContext httpContext) =>
        {
            // 密码修改仅限本人，不允许管理员代操作
            if (!httpContext.User.IsOwnResource(id))
            {
                return Results.Json(new ApiResponse(ResultCodes.Forbidden, "禁止访问"), statusCode: 403);
            }

            var user = await userService.GetByIdAsync(id);
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

            // 标记用户离线，撤销 Refresh Token
            onlineUserService.MarkUserOffline(id.ToString());
            await refreshTokenService.RevokeAsync(id);

            // 撤销当前 Access Token，强制重新登录
            var jti = httpContext.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
            if (jti != null)
            {
                tokenBlacklistService.Blacklist(jti, DateTime.UtcNow.AddMinutes(appConfig.Jwt.ExpiryMinutes));
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

        // 确认修改邮箱（公开端点，通过 token 验证）
        app.MapGet("/api/users/verify-email-change", async (
            [FromQuery] string token,
            [FromServices] IUserService userService,
            [FromServices] ITokenService tokenService) =>
        {
            if (string.IsNullOrEmpty(token))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangeEmailConfirmFail_InvalidToken, "无效的验证链接"));
            }

            var verificationToken = await tokenService.GetVerificationTokenAsync(token);

            if (verificationToken == null)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangeEmailConfirmFail_InvalidToken, "无效的验证链接"));
            }

            if (verificationToken.ExpiresAt < DateTime.UtcNow)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangeEmailConfirmFail_TokenExpired, "验证链接已过期"));
            }

            if (verificationToken.Type != VerificationTokenType.EmailChange)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.EmailVerifyFail_InvalidType, "无效的验证类型"));
            }

            if (string.IsNullOrEmpty(verificationToken.NewEmail))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangeEmailConfirmFail_NoNewEmail, "无效的新邮箱地址"));
            }

            var existingUser = await userService.ActiveEmailExistsAsync(verificationToken.NewEmail);
            if (existingUser)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangeEmailRequestFail_EmailExists, "该邮箱已被其他用户使用"));
            }

            var user = verificationToken.User;
            user.Email = verificationToken.NewEmail;
            user.EmailVerifiedAt = DateTime.UtcNow;
            await userService.UpdateUserAsync(user);

            await tokenService.DeleteVerificationTokenAsync(verificationToken.Id);

            return Results.Ok(new ApiResponse(ResultCodes.ChangeEmailConfirmSuccess, "邮箱修改成功"));
        });

        // 用户登出
        app.MapPost("/api/users/logout", async (
            [FromServices] IOnlineUserService onlineUserService,
            [FromServices] ITokenBlacklistService tokenBlacklistService,
            [FromServices] IRefreshTokenService refreshTokenService,
            [FromServices] AppConfiguration appConfig,
            HttpContext httpContext) =>
        {
            var userId = httpContext.User.GetUserId();
            if (userId.HasValue)
            {
                onlineUserService.MarkUserOffline(userId.Value.ToString());
                await refreshTokenService.RevokeAsync(userId.Value);
            }

            // 撤销当前 Token
            var jti = httpContext.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
            if (jti != null)
            {
                tokenBlacklistService.Blacklist(jti, DateTime.UtcNow.AddMinutes(appConfig.Jwt.ExpiryMinutes));
            }

            return Results.Ok(new ApiResponse(ResultCodes.Success, "已登出"));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserLogout);
    }
}
