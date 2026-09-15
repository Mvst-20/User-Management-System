using Microsoft.AspNetCore.Mvc;
using UserManagementSystem.DTOs;
using UserManagementSystem.Exceptions;
using UserManagementSystem.Extensions;
using UserManagementSystem.Services;

namespace UserManagementSystem.Endpoints;

/// <summary>
/// 邮件服务配置端点：读取（脱敏）、保存、测试发信。
/// 配置存于数据库，修改后即时生效，不再依赖部署配置文件。
/// </summary>
public static class SmtpConfigEndpoints
{
    public static void MapSmtpConfigEndpoints(this WebApplication app)
    {
        // 读取邮件服务配置（密码仅返回是否已设置）
        app.MapGet("/api/admin/smtp/config", async (
            [FromServices] ISmtpConfigService smtpConfigService) =>
        {
            var config = await smtpConfigService.GetForAdminAsync();
            return Results.Ok(new ApiResponse(ResultCodes.Success, "获取成功", config));
        }).RequireAuthorization().RequirePermission(PermissionCodes.SmtpRead);

        // 保存邮件服务配置
        app.MapPut("/api/admin/smtp/config", async (
            [FromBody] UpdateSmtpConfigRequest request,
            [FromServices] ISmtpConfigService smtpConfigService,
            HttpContext httpContext) =>
        {
            var userId = httpContext.User.GetUserId();
            if (userId is null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            var updated = await smtpConfigService.UpdateAsync(request, userId.Value);
            return Results.Ok(new ApiResponse(ResultCodes.Success, "保存成功", updated));
        }).RequireAuthorization().RequirePermission(PermissionCodes.SmtpUpdate);

        // 使用当前配置发送测试邮件
        app.MapPost("/api/admin/smtp/test", async (
            [FromBody] SendSmtpTestRequest request,
            [FromServices] IEmailService emailService,
            [FromServices] ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(request.To))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.ValidationError, "收件人邮箱不能为空"));
            }

            try
            {
                await emailService.SendEmailAsync(
                    request.To.Trim(),
                    "UCS 邮件服务测试",
                    "<p>这是一封来自 UCS 管理控制台的测试邮件。</p><p>收到本邮件说明邮件服务配置可用。</p>");

                return Results.Ok(new ApiResponse(ResultCodes.EmailSendSuccess, "测试邮件已发送，请查收"));
            }
            catch (BusinessException ex)
            {
                // 未配置/未启用等明确的业务错误
                return Results.Json(new ApiResponse(ex.Code, ex.Message), statusCode: ex.StatusCode);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "发送 SMTP 测试邮件失败（收件人 {To}）", request.To);
                return Results.BadRequest(new ApiResponse(ResultCodes.EmailSendFail, "测试邮件发送失败，请检查服务器地址与账号密码"));
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.SmtpUpdate);
    }
}
