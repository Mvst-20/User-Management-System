using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Configuration;
using UserManagementSystem.DTOs;
using UserManagementSystem.Extensions;
using UserManagementSystem.Models;
using UserManagementSystem.Services;
using UserManagementSystem.Services.Security;

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

            // 参数校验：用户名/邮箱/密码的格式与长度（失败统一 400/code=2，不落库、不触发 500）
            var usernameError = UserValidation.ValidateUsername(request.Username, out var username);
            if (usernameError != null)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, usernameError));
            }

            var emailError = UserValidation.ValidateEmail(request.Email, out var email);
            if (emailError != null)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, emailError));
            }

            var passwordError = UserValidation.ValidatePassword(request.Password);
            if (passwordError != null)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, passwordError));
            }

            // 注册速率限制（同一邮箱每分钟最多5次）
            var rateLimitKey = $"register_rate_{email}";
            if (await rateLimitService.IsLimitedAsync(rateLimitKey, 5, TimeSpan.FromMinutes(1)))
            {
                return Results.Json(new ApiResponse(ResultCodes.TooManyRequests, "请求过于频繁，请稍后再试"), statusCode: 429);
            }

            // 检查用户名是否已存在（排除已软删除用户）
            if (await userService.ActiveUsernameExistsAsync(username))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.RegisterFail_UsernameExists, "用户名已存在"));
            }

            // 检查邮箱是否已存在（排除已软删除用户）
            if (await userService.ActiveEmailExistsAsync(email))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.RegisterFail_EmailExists, "邮箱已被注册"));
            }

            // 创建用户（捕获并发写入导致的唯一键冲突）
            var user = new User
            {
                Username = username,
                Email = email,
                PasswordHash = PasswordHasher.Hash(request.Password!),
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
            [FromServices] ISessionService sessionService,
            [FromServices] ILoginRiskEvaluator riskEvaluator,
            [FromServices] ILoginAlertQueue alertQueue,
            [FromServices] IIpLocationResolver locations,
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

            var device = DeviceInfoFactory.FromRequest(context, locations);
            var user = await userService.ValidateCredentialsAsync(request.Login, request.Password);

            if (user == null)
            {
                // 登录失败，递增限流计数
                await rateLimitService.IncrementAsync(ipKey, TimeSpan.FromMinutes(5));
                await rateLimitService.IncrementAsync(accountKey, TimeSpan.FromMinutes(5));

                await sessionService.LogAsync(new LoginLog
                {
                    LoginIdentifier = request.Login,
                    EventType = LoginLogEvents.Login,
                    Success = false,
                    FailReason = "用户名或密码错误",
                    Ip = device.Ip,
                    Location = device.Location,
                    UserAgent = device.UserAgent,
                    DeviceName = device.Name
                });

                return Results.BadRequest(new ApiResponse(ResultCodes.LoginFail_UserNotFound, "用户名或密码错误"));
            }

            if (user.Status == UserStatus.Deleted)
            {
                await LogLoginRejectedAsync(sessionService, user, device, "账号不存在或已注销");
                return Results.NotFound(new ApiResponse(ResultCodes.LoginFail_AccountDeleted, "账号不存在或已注销"));
            }

            if (user.Status == UserStatus.Banned)
            {
                await LogLoginRejectedAsync(sessionService, user, device, "账号已被封禁");
                return Results.Json(new ApiResponse(ResultCodes.LoginFail_AccountBanned, "账号已被封禁"), statusCode: 403);
            }

            if (user.Status == UserStatus.Unverified)
            {
                await LogLoginRejectedAsync(sessionService, user, device, "账号未验证");
                return Results.BadRequest(new ApiResponse(ResultCodes.LoginFail_AccountUnverified, "账号未验证，请先验证邮箱"));
            }

            // 登录成功，清除此前的失败计数，避免误锁
            await rateLimitService.ResetAsync(ipKey);
            await rateLimitService.ResetAsync(accountKey);

            // 风险判定必须在创建会话之前：历史集合只应包含此前登录过的设备与地点
            var risks = await riskEvaluator.EvaluateAsync(user.Id, device);

            // 创建会话：同类设备只保留最近一次登录，其余自动失效
            var session = await sessionService.CreateSessionAsync(user, device);
            var access = jwtService.GenerateToken(user, session.Session.Id);
            await sessionService.SetLastJtiAsync(session.Session.Id, access.Jti);

            // 更新最后登录信息：只标记已跟踪实体的字段，由 CreateSessionAsync / LogAsync
            // 的 SaveChanges 一并提交，避免每次登录额外一次 SELECT + UPDATE（并发下会放大 users 行锁竞争）。
            user.LastLoginIp = device.Ip;
            user.LastLoginAt = DateTime.UtcNow;

            await sessionService.LogAsync(new LoginLog
            {
                UserId = user.Id,
                LoginIdentifier = request.Login,
                EventType = LoginLogEvents.Login,
                Success = true,
                Ip = device.Ip,
                Location = device.Location,
                UserAgent = device.UserAgent,
                DeviceName = device.Name,
                SessionId = session.Session.Id
            });

            EnqueueLoginAlert(alertQueue, appConfig, user, device, risks);

            var refreshTokenSeconds = Math.Max(1, appConfig.Jwt.RefreshTokenDays) * 86400;
            var loginData = new LoginDataResponse(access.Token, "Bearer", appConfig.Jwt.ExpiryMinutes,
                user.ToResponse(), session.RefreshToken, refreshTokenSeconds);

            return Results.Ok(new ApiResponse(ResultCodes.LoginSuccess, "登录成功", loginData));
        });

        // 刷新 Access Token（使用 Refresh Token，无需重新登录）
        app.MapPost("/api/users/refresh", async (
            [FromBody] RefreshTokenRequest request,
            [FromServices] ISessionService sessionService,
            [FromServices] IJwtService jwtService,
            [FromServices] IIpLocationResolver locations,
            [FromServices] ISystemSettingService systemSettingService,
            [FromServices] IRateLimitService rateLimitService,
            [FromServices] AppConfiguration appConfig,
            HttpContext context) =>
        {
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var refreshRateLimitKey = $"refresh_ip_{clientIp}";

            if (string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                await rateLimitService.IncrementAsync(refreshRateLimitKey, TimeSpan.FromMinutes(1));
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "Refresh Token 不能为空"));
            }

            // 检查是否允许登录
            if (!await systemSettingService.GetBoolSettingAsync("allow_login", true))
            {
                return Results.Json(new ApiResponse(ResultCodes.Forbidden, "系统暂未开放登录"), statusCode: 403);
            }

            // 刷新接口限流：无效 refresh 会触发会话查询，需防止被大量无效请求放大数据库压力。
            // 仅对无效请求计数；达到阈值后统一返回 429/code=7。
            if (await rateLimitService.GetCountAsync(refreshRateLimitKey) >= 30)
            {
                return Results.Json(new ApiResponse(ResultCodes.TooManyRequests, "请求过于频繁，请稍后再试"), statusCode: 429);
            }

            var device = DeviceInfoFactory.FromRequest(context, locations);
            var rotated = await sessionService.RotateAsync(request.RefreshToken, device);
            if (rotated == null)
            {
                await rateLimitService.IncrementAsync(refreshRateLimitKey, TimeSpan.FromMinutes(1));
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "Refresh Token 无效或已过期"), statusCode: 401);
            }

            var access = jwtService.GenerateToken(rotated.User, rotated.Session.Id);
            await sessionService.SetLastJtiAsync(rotated.Session.Id, access.Jti);

            await sessionService.LogAsync(new LoginLog
            {
                UserId = rotated.User.Id,
                EventType = LoginLogEvents.Refresh,
                Success = true,
                Ip = device.Ip,
                Location = device.Location,
                UserAgent = device.UserAgent,
                DeviceName = device.Name,
                SessionId = rotated.Session.Id
            });

            var refreshTokenSeconds = Math.Max(1, appConfig.Jwt.RefreshTokenDays) * 86400;
            var data = new LoginDataResponse(access.Token, "Bearer", appConfig.Jwt.ExpiryMinutes,
                rotated.User.ToResponse(), rotated.RefreshToken, refreshTokenSeconds);

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

        // 用户登出：只撤销当前设备会话，其他设备保持登录
        app.MapPost("/api/users/logout", async (
            [FromServices] ISessionService sessionService,
            [FromServices] ITokenBlacklistService tokenBlacklistService,
            [FromServices] AppConfiguration appConfig,
            HttpContext httpContext) =>
        {
            var userId = httpContext.User.GetUserId();
            var sessionId = httpContext.User.GetSessionId();

            if (userId.HasValue && !string.IsNullOrEmpty(sessionId))
            {
                await sessionService.RevokeSessionAsync(userId.Value, sessionId, SessionRevokeReasons.Logout);
            }

            // 撤销当前 Token
            var jti = httpContext.User.GetJti();
            if (jti != null)
            {
                await tokenBlacklistService.BlacklistAsync(jti, DateTime.UtcNow.AddMinutes(appConfig.Jwt.ExpiryMinutes));
            }

            return Results.Ok(new ApiResponse(ResultCodes.Success, "已登出"));
        }).RequireAuthorization().RequirePermission(PermissionCodes.UserLogout);

        // ============ 内部辅助 ============

        // 被拒登录的审计记录：写失败不影响返回
        static Task LogLoginRejectedAsync(ISessionService sessionService, User user, DeviceInfo device, string reason)
            => sessionService.LogAsync(new LoginLog
            {
                UserId = user.Id,
                LoginIdentifier = user.Username,
                EventType = LoginLogEvents.Login,
                Success = false,
                FailReason = reason,
                Ip = device.Ip,
                Location = device.Location,
                UserAgent = device.UserAgent,
                DeviceName = device.Name
            });

        // 命中风险时异步入队提醒；登录响应不等待邮件发送
        static void EnqueueLoginAlert(
            ILoginAlertQueue alertQueue,
            AppConfiguration appConfig,
            User user,
            DeviceInfo device,
            IReadOnlyList<string> risks)
        {
            if (risks.Count == 0 || string.IsNullOrWhiteSpace(user.Email))
            {
                return;
            }

            var enabled = risks.Where(risk => risk == LoginRiskKinds.NewDevice
                ? appConfig.Security.AlertOnNewDevice
                : appConfig.Security.AlertOnNewLocation).ToList();

            if (enabled.Count == 0)
            {
                return;
            }

            alertQueue.TryEnqueue(new LoginAlertMessage(
                user.Id,
                user.Username,
                user.Email,
                enabled,
                device.Name,
                device.Ip,
                device.Location,
                DateTime.UtcNow));
        }
    }
}
