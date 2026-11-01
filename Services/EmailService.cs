using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using UserManagementSystem.Configuration;
using UserManagementSystem.DTOs;
using UserManagementSystem.Exceptions;

namespace UserManagementSystem.Services;

/// <summary>
/// 邮件发送服务。
/// SMTP 连接参数来自数据库配置（<see cref="ISmtpConfigService"/>），改动即时生效；
/// <see cref="AppConfiguration"/> 仅用于站点地址与验证链接有效期。
/// </summary>
public class EmailService : IEmailService
{
    private readonly AppConfiguration _config;
    private readonly ISmtpConfigService _smtpConfigService;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        AppConfiguration config,
        ISmtpConfigService smtpConfigService,
        ILogger<EmailService> logger)
    {
        _config = config;
        _smtpConfigService = smtpConfigService;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        var options = await _smtpConfigService.GetEffectiveAsync();
        if (options is null)
        {
            _logger.LogWarning("Skip sending email to {Recipient}: SMTP is not configured or disabled", to);
            throw new BusinessException(ResultCodes.EmailSendFail, "邮件服务未配置或已停用，请先在管理后台完成邮件服务配置");
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                string.IsNullOrWhiteSpace(options.FromName) ? "User Management System" : options.FromName,
                options.FromAddress));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();

            // 保持既有行为：465 → SSL 直连；587 → STARTTLS；其他端口按配置
            var secureSocketOptions = options.Port switch
            {
                465 => SecureSocketOptions.SslOnConnect,
                587 => SecureSocketOptions.StartTls,
                _ => options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls
            };

            await client.ConnectAsync(options.Host, options.Port, secureSocketOptions);

            if (!string.IsNullOrEmpty(options.Username))
            {
                await client.AuthenticateAsync(options.Username, options.Password);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent successfully to {Recipient}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Recipient}", to);
            throw new BusinessException(ResultCodes.EmailSendFail, $"邮件发送失败：{ex.Message}");
        }
    }

    public async Task SendVerificationEmailAsync(string to, string username, string token, bool isEmailChange = false, string? newEmail = null)
    {
        string subject;
        string htmlBody;

        var expiryMinutes = _config.AppSettings.TokenExpiryMinutes;

        // 用户可控数据（用户名、新邮箱）必须转义后再拼入 HTML 正文，
        // 否则邮件客户端会把用户名中的标签当作邮件正文解析（与登录提醒邮件保持一致）。
        var safeUsername = WebUtility.HtmlEncode(username);
        var safeToken = Uri.EscapeDataString(token);

        if (isEmailChange && !string.IsNullOrEmpty(newEmail))
        {
            subject = "Confirm Email Change";
            var safeNewEmail = WebUtility.HtmlEncode(newEmail);
            var verifyUrl = $"{_config.AppSettings.ApiBaseUrl}/api/users/verify-email-change?token={safeToken}";

            htmlBody = $@"<!DOCTYPE html>
<html><head><meta charset=""utf-8""></head>
<body style=""font-family:Arial,sans-serif;margin:0;padding:20px;background:#f5f5f5;"">
<div style=""max-width:600px;margin:0 auto;background:#fff;padding:30px;border-radius:8px;"">
<h2 style=""color:#333;margin:0 0 20px;"">Confirm Email Change</h2>
<p>Hello {safeUsername},</p>
<p>You are requesting to change your email to: <strong>{safeNewEmail}</strong></p>
<p><a href=""{verifyUrl}"" style=""display:inline-block;background:#7b1fa2;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px;"">Confirm Change</a></p>
<p style=""color:#666;font-size:14px;"">Or copy this link: {verifyUrl}</p>
<p style=""color:#666;font-size:13px;"">This link expires in {expiryMinutes} minutes.</p>
<p style=""color:#e65100;font-size:13px;""><strong>Security Notice:</strong> If you did not request this, please ignore this email.</p>
</div></body></html>";
        }
        else
        {
            subject = "Email Verification";
            var verifyUrl = $"{_config.AppSettings.ApiBaseUrl}/api/users/verify-email?token={safeToken}";

            htmlBody = $@"<!DOCTYPE html>
<html><head><meta charset=""utf-8""></head>
<body style=""font-family:Arial,sans-serif;margin:0;padding:20px;background:#f5f5f5;"">
<div style=""max-width:600px;margin:0 auto;background:#fff;padding:30px;border-radius:8px;"">
<h2 style=""color:#333;margin:0 0 20px;"">Verify Your Email</h2>
<p>Hello {safeUsername},</p>
<p>Thank you for registering. Click the button below to verify your email address (expires in {expiryMinutes} minutes).</p>
<p><a href=""{verifyUrl}"" style=""display:inline-block;background:#1976d2;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px;"">Verify Email</a></p>
<p style=""color:#666;font-size:14px;"">Or copy this link: {verifyUrl}</p>
<p style=""color:#666;font-size:13px;"">If you did not create an account, please ignore this email.</p>
</div></body></html>";
        }

        await SendEmailAsync(to, subject, htmlBody);
    }
}
