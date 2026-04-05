using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Extensions;
using UserManagementSystem.Models;
using UserManagementSystem.Services;

namespace UserManagementSystem.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        // ============ 公开端点 ============

        // 用户注册
        app.MapPost("/api/users/register", async (
            [FromBody] RegisterRequest request,
            [FromServices] IUserService userService,
            [FromServices] ITokenService tokenService,
            [FromServices] IEmailService emailService,
            [FromServices] ILogger<Program> logger) =>
        {
            // 检查用户名是否已存在
            if (await userService.GetByUsernameAsync(request.Username) != null)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.RegisterFail_UsernameExists, "用户名已存在"));
            }

            // 检查邮箱是否已存在
            if (await userService.GetByEmailAsync(request.Email) != null)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.RegisterFail_EmailExists, "邮箱已被注册"));
            }

            // 创建用户
            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Status = UserStatus.Unverified,
                Role = UserRole.User,
                CreatedAt = DateTime.UtcNow,
                Extras = request.Extras?.GetRawText()
            };

            await userService.CreateUserAsync(user);

            // 生成验证Token
            var token = await tokenService.CreateVerificationTokenAsync(user.Id, VerificationTokenType.Registration);

            // 发送验证邮件
            try
            {
                await emailService.SendVerificationEmailAsync(user.Email, user.Username, token.Token);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send verification email");
            }

            return Results.Ok(new ApiResponse(ResultCodes.RegisterSuccess, "注册成功，请查收验证邮件", 
                new RegisterDataResponse(user.Email)));
        });

        // 用户登录
        app.MapPost("/api/users/login", async (
            [FromBody] LoginRequest request,
            [FromServices] IUserService userService,
            [FromServices] IJwtService jwtService,
            HttpContext context) =>
        {
            var user = await userService.ValidateCredentialsAsync(request.Login, request.Password);

            if (user == null)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.LoginFail_UserNotFound, "用户名或密码错误"));
            }

            // 软删除用户不能登录
            if (user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.LoginFail_AccountDeleted, "账号不存在或已注销"));
            }

            // 更新最后登录信息
            user.LastLoginIp = context.Connection.RemoteIpAddress?.ToString();
            user.LastLoginAt = DateTime.UtcNow;
            await userService.UpdateUserAsync(user);

            // 生成Token
            var jwtToken = jwtService.GenerateToken(user);

            var loginData = new LoginDataResponse(jwtToken, "Bearer", 120, user.ToResponse());
            var message = user.Status == UserStatus.Banned ? "登录成功（账号已被封禁）" : "登录成功";

            return Results.Ok(new ApiResponse(ResultCodes.LoginSuccess, message, loginData));
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
            // 检查频率限制
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

            // 检查是否有未过期的Token
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var validToken = await dbContext.EmailVerificationTokens
                .Where(t => t.UserId == user.Id && 
                           t.Type == VerificationTokenType.Registration && 
                           t.ExpiresAt > DateTime.UtcNow)
                .FirstOrDefaultAsync();

            if (validToken == null)
            {
                var token = await tokenService.CreateVerificationTokenAsync(user.Id, VerificationTokenType.Registration);
                try
                {
                    await emailService.SendVerificationEmailAsync(user.Email, user.Username, token.Token);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send verification email");
                    return Results.BadRequest(new ApiResponse(ResultCodes.EmailSendFail, "邮件发送失败"));
                }
            }

            // 设置频率限制缓存（1分钟）
            cache.Set(cacheKey, true, TimeSpan.FromMinutes(1));

            return Results.Ok(new ApiResponse(ResultCodes.ResendVerificationSuccess, "验证邮件已发送"));
        });

        // ============ 需要认证的端点 ============

        // 获取单个用户信息
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
        }).RequireAuthorization();

        // 完全更新用户信息
        app.MapPut("/api/users/{id}", async (
            ulong id,
            [FromBody] UpdateUserRequest request,
            [FromServices] IUserService userService,
            HttpContext httpContext) =>
        {
            var currentUser = httpContext.User;
            var user = await userService.GetByIdAsync(id);

            if (user == null || user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));
            }

            // 权限检查
            if (!currentUser.IsOwnResource(id) && !currentUser.IsAdmin())
            {
                return Results.StatusCode(403);
            }

            // 状态检查
            if (user.Status == UserStatus.Banned || user.Status == UserStatus.Deleted)
            {
                return Results.StatusCode(403);
            }

            // 检查用户名唯一性
            if (!string.IsNullOrEmpty(request.Username) && request.Username != user.Username)
            {
                if (await userService.GetByUsernameAsync(request.Username) != null)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_UsernameExists, "用户名已存在"));
                }
                user.Username = request.Username;
            }

            // 检查邮箱唯一性
            if (!string.IsNullOrEmpty(request.Email) && request.Email != user.Email)
            {
                if (await userService.GetByEmailAsync(request.Email) != null)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_EmailExists, "邮箱已被使用"));
                }
                user.Email = request.Email;
            }

            if (request.Phone != null)
            {
                var existingUser = await userService.GetByPhoneAsync(request.Phone);
                if (existingUser != null && existingUser.Id != id)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_PhoneExists, "手机号已被使用"));
                }
                user.Phone = request.Phone;
            }

            // 只有管理员可以修改角色
            if (request.Role.HasValue && currentUser.IsAdmin())
            {
                user.Role = (UserRole)request.Role.Value;
            }

            if (request.Extras.HasValue)
            {
                user.Extras = request.Extras.Value.GetRawText();
            }

            await userService.UpdateUserAsync(user);

            return Results.Ok(new ApiResponse(ResultCodes.UpdateUserSuccess, "更新成功", user.ToResponse()));
        }).RequireAuthorization();

        // 部分更新用户信息
        app.MapPatch("/api/users/{id}", async (
            ulong id,
            [FromBody] PatchUserRequest request,
            [FromServices] IUserService userService,
            HttpContext httpContext) =>
        {
            var currentUser = httpContext.User;
            var user = await userService.GetByIdAsync(id);

            if (user == null || user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));
            }

            if (!currentUser.IsOwnResource(id) && !currentUser.IsAdmin())
            {
                return Results.StatusCode(403);
            }

            if (user.Status == UserStatus.Banned || user.Status == UserStatus.Deleted)
            {
                return Results.StatusCode(403);
            }

            if (!string.IsNullOrEmpty(request.Username) && request.Username != user.Username)
            {
                if (await userService.GetByUsernameAsync(request.Username) != null)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_UsernameExists, "用户名已存在"));
                }
                user.Username = request.Username;
            }

            if (!string.IsNullOrEmpty(request.Email) && request.Email != user.Email)
            {
                if (await userService.GetByEmailAsync(request.Email) != null)
                {
                    return Results.BadRequest(new ApiResponse(ResultCodes.UpdateUserFail_EmailExists, "邮箱已被使用"));
                }
                user.Email = request.Email;
            }

            if (request.Phone != null)
            {
                var existingUser = await userService.GetByPhoneAsync(request.Phone);
                if (existingUser != null && existingUser.Id != id)
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
        }).RequireAuthorization();

        // 软删除用户（仅管理员）
        app.MapDelete("/api/users/{id}", async (
            ulong id,
            [FromServices] IUserService userService,
            HttpContext httpContext) =>
        {
            if (!httpContext.User.IsAdmin())
            {
                return Results.StatusCode(403);
            }

            var result = await userService.DeleteUserAsync(id);

            if (!result)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.DeleteUserFail_NotFound, "用户不存在"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.DeleteUserSuccess, "用户已删除"));
        }).RequireAuthorization();

        // 分页查询用户列表（仅管理员）
        app.MapGet("/api/users", async (
            [FromServices] IUserService userService,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] int? status = null,
            [FromQuery] int? role = null,
            [FromQuery] string? search = null) =>
        {
            var (users, totalCount) = await userService.GetUsersAsync(page, pageSize, status, role, search);

            var response = new PagedApiResponse(
                ResultCodes.GetUsersSuccess,
                "获取成功",
                users.Select(u => (object)u.ToResponse()).ToList(),
                page,
                pageSize,
                totalCount
            );

            return Results.Ok(response);
        }).RequireAuthorization("AdminOnly");

        // 修改密码
        app.MapPost("/api/users/{id}/change-password", async (
            ulong id,
            [FromBody] ChangePasswordRequest request,
            [FromServices] IUserService userService,
            HttpContext httpContext) =>
        {
            if (!httpContext.User.IsOwnResource(id))
            {
                return Results.StatusCode(403);
            }

            var user = await userService.GetByIdAsync(id);

            if (user == null || user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));
            }

            if (user.Status == UserStatus.Banned || user.Status == UserStatus.Deleted)
            {
                return Results.StatusCode(403);
            }

            if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangePasswordFail_WrongCurrent, "当前密码错误"));
            }

            if (request.NewPassword == request.CurrentPassword)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangePasswordFail_SamePassword, "新密码不能与当前密码相同"));
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            await userService.UpdateUserAsync(user);

            return Results.Ok(new ApiResponse(ResultCodes.ChangePasswordSuccess, "密码修改成功"));
        }).RequireAuthorization();

        // 申请修改邮箱
        app.MapPost("/api/users/{id}/change-email", async (
            ulong id,
            [FromBody] ChangeEmailRequest request,
            [FromServices] IUserService userService,
            [FromServices] ITokenService tokenService,
            [FromServices] IEmailService emailService,
            HttpContext httpContext,
            [FromServices] ILogger<Program> logger) =>
        {
            if (!httpContext.User.IsOwnResource(id))
            {
                return Results.StatusCode(403);
            }

            var user = await userService.GetByIdAsync(id);

            if (user == null || user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));
            }

            if (user.Status == UserStatus.Banned || user.Status == UserStatus.Deleted)
            {
                return Results.StatusCode(403);
            }

            // 检查是否与旧邮箱相同
            if (request.NewEmail == user.Email)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangeEmailRequestFail_SameEmail, "新邮箱与旧邮箱相同"));
            }

            // 验证密码
            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangeEmailRequestFail_WrongPassword, "密码错误"));
            }

            // 检查新邮箱唯一性
            if (await userService.GetByEmailAsync(request.NewEmail) != null)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ChangeEmailRequestFail_EmailExists, "该邮箱已被使用"));
            }

            // 生成验证Token
            var token = await tokenService.CreateVerificationTokenAsync(
                id, 
                VerificationTokenType.EmailChange, 
                request.NewEmail);

            // 发送验证邮件
            try
            {
                await emailService.SendVerificationEmailAsync(
                    request.NewEmail, 
                    user.Username, 
                    token.Token, 
                    true, 
                    request.NewEmail);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send email change verification email");
                return Results.BadRequest(new ApiResponse(ResultCodes.EmailSendFail, "邮件发送失败"));
            }

            return Results.Ok(new ApiResponse(ResultCodes.ChangeEmailRequestSuccess, "验证邮件已发送到新邮箱，请查收"));
        }).RequireAuthorization();

        // 确认修改邮箱
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

            // 更新用户邮箱
            var user = verificationToken.User;
            user.Email = verificationToken.NewEmail;
            await userService.UpdateUserAsync(user);

            // 删除Token
            await tokenService.DeleteVerificationTokenAsync(verificationToken.Id);

            return Results.Ok(new ApiResponse(ResultCodes.ChangeEmailConfirmSuccess, "邮箱修改成功"));
        });
    }
}
