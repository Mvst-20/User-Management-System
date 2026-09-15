using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Configuration;
using UserManagementSystem.DTOs;
using UserManagementSystem.Extensions;
using UserManagementSystem.Models;
using UserManagementSystem.Services;

namespace UserManagementSystem.Endpoints;

/// <summary>
/// 认证与账户生命周期端点：注册、登录、Token 刷新、邮箱验证、登出。
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        // 用户注册
        app.MapPost("/api/users/register", async (
            [FromBody] RegisterRequest request,
            [FromServices] IUserService userService,
            [FromServices] ITokenService tokenService,
            [FromServices] IEmailService emailService,
            [FromServices] IUserGroupService userGroupService,
            [FromServices] ISystemSettingService systemSettingService,
            [FromServices] IRateLimitService rateLimitService,
            [FromServices] ILogger<Program> logger) =>
        {
            // 检查是否允许注册
            if (!await systemSettingService.GetBoolSettingAsync("allow_register", true))
            {
                return Results.Json(new ApiResponse(ResultCodes.Forbidden, "系统暂未开放注册"), statusCode: 403);
            }

            // 注册速率限制（同一邮箱每分钟最多5次）
            var rateLimitKey = $"register_rate_{request.Email}";
            if (await rateLimitService.IsLimitedAsync(rateLimitKey, 5, TimeSpan.FromMinutes(1)))
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

            // 创建用户（捕获并发写入导致的唯一键冲突）
            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12),
                Status = UserStatus.Unverified,
                CreatedAt = DateTime.UtcNow,
                Extras = request.Extras?.GetRawText()
            };

            try
            {
                await userService.CreateUserAsync(user);
            }
            catch (DbUpdateException ex) when (ex.InnerException is MySqlConnector.MySqlException mysqlEx && mysqlEx.ErrorCode == MySqlConnector.MySqlErrorCode.DuplicateKeyEntry)
            {
                // 并发注册导致唯一键冲突，返回友好提示
                return Results.BadRequest(new ApiResponse(ResultCodes.RegisterFail_EmailExists, "该用户名或邮箱已被注册"));
            }

            // 自动加入所有开启了「基本用户权限」的权限组
            // （内置的"普通用户"组默认开启该标记，由 PermissionService.SeedGroupsIfMissingAsync 播种/回填）
            var defaultGroups = await userGroupService.GetDefaultGroupsForNewUsersAsync();
            foreach (var defaultGroup in defaultGroups)
            {
                await userGroupService.AddMemberAsync(defaultGroup.Id, user.Id);
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
            [FromServices] IRateLimitService rateLimitService,
            [FromServices] AppConfiguration appConfig,
            HttpContext context) =>
        {
            // 检查是否允许登录
            if (!await systemSettingService.GetBoolSettingAsync("allow_login", true))
            {
                return Results.Json(new ApiResponse(ResultCodes.Forbidden, "系统暂未开放登录"), statusCode: 403);
            }

            // 登录限流 key
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var loginKey = request.Login;
            var ipKey = $"login_ip_{clientIp}";
            var accountKey = $"login_account_{loginKey}";

            // IP 限流检查：同一 IP 5 分钟内最多 20 次
            if (await rateLimitService.GetCountAsync(ipKey) >= 20)
                return Results.Json(new ApiResponse(ResultCodes.LoginFail_RateLimited, "登录尝试过于频繁，请稍后再试"), statusCode: 429);

            // 账号限流检查：同一账号 5 分钟内最多 5 次
            if (await rateLimitService.GetCountAsync(accountKey) >= 5)
                return Results.Json(new ApiResponse(ResultCodes.LoginFail_RateLimited, "登录尝试过于频繁，请稍后再试"), statusCode: 429);

            var user = await userService.ValidateCredentialsAsync(request.Login, request.Password);

            if (user == null)
            {
                // 登录失败，递增限流计数
                await rateLimitService.IncrementAsync(ipKey, TimeSpan.FromMinutes(5));
                await rateLimitService.IncrementAsync(accountKey, TimeSpan.FromMinutes(5));
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

            // 登录成功，清除此前的失败计数，避免误锁
            await rateLimitService.ResetAsync(ipKey);
            await rateLimitService.ResetAsync(accountKey);

            // 更新最后登录信息
            user.LastLoginIp = context.Connection.RemoteIpAddress?.ToString();
            user.LastLoginAt = DateTime.UtcNow;
            await userService.UpdateUserAsync(user);

            // 标记用户在线
            await onlineUserService.MarkUserOnlineAsync(user.Id.ToString());

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

            // 更新用户状态（仅未验证账号可完成注册验证，防止已封禁/已删除账号被"复活"）
            var user = verificationToken.User;
            if (user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.LoginFail_AccountDeleted, "账号不存在或已注销"));
            }
            if (user.Status == UserStatus.Banned)
            {
                return Results.Json(new ApiResponse(ResultCodes.LoginFail_AccountBanned, "账号已被封禁"), statusCode: 403);
            }

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
            [FromServices] IRateLimitService rateLimitService,
            [FromServices] ILogger<Program> logger) =>
        {
            // 同一邮箱每分钟最多重发 1 次
            var rateLimitKey = $"resend_verification_{request.Email}";
            if (await rateLimitService.GetCountAsync(rateLimitKey) >= 1)
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

            await rateLimitService.IncrementAsync(rateLimitKey, TimeSpan.FromMinutes(1));

            return Results.Ok(new ApiResponse(ResultCodes.ResendVerificationSuccess, "验证邮件已发送"));
        });

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
            if (user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.LoginFail_AccountDeleted, "账号不存在或已注销"));
            }
            if (user.Status == UserStatus.Banned)
            {
                return Results.Json(new ApiResponse(ResultCodes.LoginFail_AccountBanned, "账号已被封禁"), statusCode: 403);
            }

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
                await onlineUserService.MarkUserOfflineAsync(userId.Value.ToString());
                await refreshTokenService.RevokeAsync(userId.Value);
            }

            // 撤销当前 Token
            var jti = httpContext.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
            if (jti != null)
            {
                await tokenBlacklistService.BlacklistAsync(jti, DateTime.UtcNow.AddMinutes(appConfig.Jwt.ExpiryMinutes));
            }

            return Results.Ok(new ApiResponse(ResultCodes.Success, "已登出"));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserLogout);
    }
}
