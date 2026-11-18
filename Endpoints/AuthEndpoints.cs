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
            [FromServices] AppConfiguration appConfig,
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
                if (appConfig.Security.UniformAuthErrors)
                {
                    PasswordHasher.VerifyDummy(request.Password);
                    return UniformRegistrationAccepted();
                }

                return Results.BadRequest(new ApiResponse(ResultCodes.RegisterFail_UsernameExists, "用户名已存在"));
            }

            // 检查邮箱是否已存在（排除已软删除用户）
            if (await userService.ActiveEmailExistsAsync(email))
            {
                if (appConfig.Security.UniformAuthErrors)
                {
                    PasswordHasher.VerifyDummy(request.Password);
                    return UniformRegistrationAccepted();
                }

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
                // 并发注册导致唯一键冲突：统一模式下必须返回与成功/重复注册一致的响应，
                // 否则攻击者可通过“极窄时间窗”区分账号是否已存在。
                if (appConfig.Security.UniformAuthErrors)
                {
                    return UniformRegistrationAccepted();
                }

                return Results.BadRequest(new ApiResponse(ResultCodes.RegisterFail_EmailExists, "该用户名或邮箱已被注册"));
            }

            // 自动加入所有开启了「基本用户权限」的权限组
            // （内置的"普通用户"组默认开启该标记，由 PermissionService.SeedGroupsIfMissingAsync 播种/回填）
            var defaultGroups = await userGroupService.GetDefaultGroupsForNewUsersAsync();
            foreach (var defaultGroup in defaultGroups)
            {
                await userGroupService.AddMemberAsync(defaultGroup.Id, user.Id);
            }

            // 生成验证Token（数据库只保存哈希，明文仅用于本次邮件）
            var tokenIssue = await tokenService.CreateVerificationTokenAsync(user.Id, VerificationTokenType.Registration);

            // 发送验证邮件
            bool emailSent = true;
            try
            {
                await emailService.SendVerificationEmailAsync(user.Email, user.Username, tokenIssue.PlainToken);
            }
            catch (Exception ex)
            {
                emailSent = false;
                logger.LogError(ex, "Failed to send verification email to {Email}", user.Email);
            }

            var message = emailSent
                ? "注册成功，请查收验证邮件"
                : "注册成功，但验证邮件发送失败，请稍后重新发送验证邮件";

            // 统一模式下不暴露 emailSent / SMTP 成功失败，也不暴露账号是否存在；
            // 真实发送结果只写服务端日志，避免重复注册通知邮件与验证邮件的差异成为枚举面。
            if (appConfig.Security.UniformAuthErrors)
            {
                return UniformRegistrationAccepted();
            }

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
            var login = (request.Login ?? string.Empty).Trim();
            if (login.Length == 0 || string.IsNullOrEmpty(request.Password))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "请输入账号与密码"));
            }

            // 账号维度使用规范化后的 key，避免大小写/首尾空白绕过计数
            var ipKey = $"login_ip_{clientIp}";
            var accountKey = $"login_account_{login.ToLowerInvariant()}";

            // 先原子递增再校验：并发请求在数据库层串行化计数，
            // 不会出现“100 个请求同时读到 0、全部通过检查”的绕过。
            // 限制：IP 20 次/5 分钟；账号 5 次/5 分钟。
            if (await rateLimitService.IsLimitedAsync(ipKey, 20, TimeSpan.FromMinutes(5)))
            {
                return Results.Json(new ApiResponse(ResultCodes.LoginFail_RateLimited, "登录尝试过于频繁，请稍后再试"), statusCode: 429);
            }

            if (await rateLimitService.IsLimitedAsync(accountKey, 5, TimeSpan.FromMinutes(5)))
            {
                return Results.Json(new ApiResponse(ResultCodes.LoginFail_RateLimited, "登录尝试过于频繁，请稍后再试"), statusCode: 429);
            }

            var device = DeviceInfoFactory.FromRequest(context, locations);
            var user = await userService.ValidateCredentialsAsync(login, request.Password);

            if (user == null)
            {
                await sessionService.LogAsync(new LoginLog
                {
                    LoginIdentifier = login,
                    EventType = LoginLogEvents.Login,
                    Success = false,
                    FailReason = "用户名或密码错误",
                    Ip = device.Ip,
                    Location = device.Location,
                    UserAgent = device.UserAgent,
                    DeviceName = device.Name
                });

                return LoginFailureResult(appConfig, 400, ResultCodes.LoginFail_UserNotFound, "用户名或密码错误");
            }

            if (user.Status == UserStatus.Deleted)
            {
                await LogLoginRejectedAsync(sessionService, user, device, "账号不存在或已注销");
                return LoginFailureResult(appConfig, 404, ResultCodes.LoginFail_AccountDeleted, "账号不存在或已注销");
            }

            if (user.Status == UserStatus.Banned)
            {
                await LogLoginRejectedAsync(sessionService, user, device, "账号已被封禁");
                return LoginFailureResult(appConfig, 403, ResultCodes.LoginFail_AccountBanned, "账号已被封禁");
            }

            if (user.Status == UserStatus.Unverified)
            {
                await LogLoginRejectedAsync(sessionService, user, device, "账号未验证");
                return LoginFailureResult(appConfig, 400, ResultCodes.LoginFail_AccountUnverified, "账号未验证，请先验证邮箱");
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
                LoginIdentifier = login,
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

            // 检查是否允许登录
            if (!await systemSettingService.GetBoolSettingAsync("allow_login", true))
            {
                return Results.Json(new ApiResponse(ResultCodes.Forbidden, "系统暂未开放登录"), statusCode: 403);
            }

            // 刷新接口限流：原子预递增，避免并发请求同时读到旧计数。
            // 同一 IP 每分钟最多 30 次刷新（含无效令牌），超限返回 429/code=7。
            if (await rateLimitService.IsLimitedAsync(refreshRateLimitKey, 30, TimeSpan.FromMinutes(1)))
            {
                return Results.Json(new ApiResponse(ResultCodes.TooManyRequests, "请求过于频繁，请稍后再试"), statusCode: 429);
            }

            if (string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "Refresh Token 不能为空"));
            }

            var device = DeviceInfoFactory.FromRequest(context, locations);
            var rotated = await sessionService.RotateAsync(request.RefreshToken, device);
            if (rotated == null)
            {
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

            var verificationToken = await tokenService.ConsumeVerificationTokenAsync(token);

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
            var user = await userService.GetByIdAsync(verificationToken.UserId);
            if (user == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.LoginFail_AccountDeleted, "账号不存在或已注销"));
            }
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

            // Token 已在 ConsumeVerificationTokenAsync 中原子删除（单次使用）
            return Results.Ok(new ApiResponse(ResultCodes.EmailVerifySuccess, "邮箱验证成功"));
        });

        // 重新发送验证邮件
        app.MapPost("/api/users/resend-verification", async (
            [FromBody] ResendVerificationRequest request,
            [FromServices] IUserService userService,
            [FromServices] ITokenService tokenService,
            [FromServices] IEmailService emailService,
            [FromServices] IRateLimitService rateLimitService,
            [FromServices] AppConfiguration appConfig,
            [FromServices] ILogger<Program> logger) =>
        {
            var email = (request.Email ?? string.Empty).Trim();
            if (email.Length == 0)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "邮箱不能为空"));
            }

            // 同一邮箱每分钟最多重发 1 次（原子预递增，避免并发绕过）
            var rateLimitKey = $"resend_verification_{email.ToLowerInvariant()}";
            if (await rateLimitService.IsLimitedAsync(rateLimitKey, 1, TimeSpan.FromMinutes(1)))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ResendVerificationFail_RateLimited, "请稍后再试"));
            }

            var user = await userService.GetByEmailAsync(email);

            if (appConfig.Security.UniformAuthErrors)
            {
                // 未知邮箱、已删除、已验证、未验证都返回同一个响应；
                // 仅“未验证”账号实际发送邮件，发送失败也只写日志，不通过响应暴露账号状态。
                if (user is { Status: UserStatus.Unverified })
                {
                    try
                    {
                        var newToken = await tokenService.CreateVerificationTokenAsync(
                            user.Id, VerificationTokenType.Registration);
                        await emailService.SendVerificationEmailAsync(user.Email, user.Username, newToken.PlainToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to resend verification email");
                    }
                }

                return Results.Ok(new ApiResponse(ResultCodes.ResendVerificationSuccess,
                    "如果该邮箱需要验证，验证邮件将发送"));
            }

            if (user == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.ResendVerificationFail_UserNotFound, "用户不存在"));
            }

            if (user.Status != UserStatus.Unverified)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ResendVerificationFail_AlreadyVerified, "该用户无需验证"));
            }

            var legacyToken = await tokenService.CreateVerificationTokenAsync(user.Id, VerificationTokenType.Registration);
            try
            {
                await emailService.SendVerificationEmailAsync(user.Email, user.Username, legacyToken.PlainToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send verification email");
                return Results.BadRequest(new ApiResponse(ResultCodes.EmailSendFail, "邮件发送失败"));
            }

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

            var verificationToken = await tokenService.ConsumeVerificationTokenAsync(token);

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

            var user = await userService.GetByIdAsync(verificationToken.UserId);
            if (user == null)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.LoginFail_AccountDeleted, "账号不存在或已注销"));
            }
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

        // 统一认证失败响应：不泄露账号是否存在/封禁/未验证；旧客户端可通过 Security:UniformAuthErrors=false 恢复精确错误码。
        // 使用 400 而不是 401：管理前端 UcsApiClient 会把 401 当作“会话失效”触发刷新/跳转，
        // 而这里的失败是凭据错误，不需要走会话恢复流程。
        static IResult LoginFailureResult(AppConfiguration appConfig, int legacyStatus, int legacyCode, string legacyMessage)
        {
            if (appConfig.Security.UniformAuthErrors)
            {
                return Results.Json(new ApiResponse(ResultCodes.LoginFail_UserNotFound, "账号或密码错误"), statusCode: 400);
            }

            return Results.Json(new ApiResponse(legacyCode, legacyMessage), statusCode: legacyStatus);
        }

        // 统一模式下的注册响应：无论用户名/邮箱是否已存在、SMTP 是否成功，
        // 都返回完全一致的 200/code=100，不包含 emailSent 等可区分字段。
        static IResult UniformRegistrationAccepted()
            => Results.Ok(new ApiResponse(ResultCodes.RegisterSuccess,
                "注册请求已受理，如果信息可用，系统将发送验证邮件"));

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
