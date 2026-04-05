using MailKit.Net.Smtp;
using MimeKit;
using UserManagementSystem.Configuration;

namespace UserManagementSystem.Services;

public class EmailService : IEmailService
{
    private readonly AppConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(AppConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                _config.Smtp.FromName ?? "User Management System",
                _config.Smtp.From
            ));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            
            // 根据端口自动选择连接方式
            var secureSocketOptions = _config.Smtp.Port switch
            {
                465 => MailKit.Security.SecureSocketOptions.SslOnConnect,
                587 => MailKit.Security.SecureSocketOptions.StartTls,
                _ => _config.Smtp.UseSsl ? MailKit.Security.SecureSocketOptions.SslOnConnect : MailKit.Security.SecureSocketOptions.None
            };
            
            await client.ConnectAsync(_config.Smtp.Host, _config.Smtp.Port, secureSocketOptions);

            if (!string.IsNullOrEmpty(_config.Smtp.Username))
            {
                await client.AuthenticateAsync(_config.Smtp.Username, _config.Smtp.Password);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent successfully to {Recipient}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Recipient}", to);
            throw new InvalidOperationException($"Failed to send email: {ex.Message}", ex);
        }
    }

    public async Task SendVerificationEmailAsync(string to, string username, string token, bool isEmailChange = false, string? newEmail = null)
    {
        string subject;
        string htmlBody;

        if (isEmailChange && !string.IsNullOrEmpty(newEmail))
        {
            subject = "Confirm Email Change";
            var verifyUrl = $"{_config.AppSettings.ApiBaseUrl}/api/users/verify-email-change?token={token}";
            
            htmlBody = $@"<!DOCTYPE html>
<html><head><meta charset=""utf-8""></head>
<body style=""font-family:Arial,sans-serif;margin:0;padding:20px;background:#f5f5f5;"">
<div style=""max-width:600px;margin:0 auto;background:#fff;padding:30px;border-radius:8px;"">
<h2 style=""color:#333;margin:0 0 20px;"">Confirm Email Change</h2>
<p>Hello {username},</p>
<p>You are requesting to change your email to: <strong>{newEmail}</strong></p>
<p><a href=""{verifyUrl}"" style=""display:inline-block;background:#7b1fa2;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px;"">Confirm Change</a></p>
<p style=""color:#666;font-size:14px;"">Or copy this link: {verifyUrl}</p>
<p style=""color:#e65100;font-size:13px;""><strong>Security Notice:</strong> If you did not request this, please ignore this email.</p>
</div></body></html>";
        }
        else
        {
            subject = "Email Verification";
            var verifyUrl = $"{_config.AppSettings.ApiBaseUrl}/api/users/verify-email?token={token}";
            
            htmlBody = $@"<!DOCTYPE html>
<html><head><meta charset=""utf-8""></head>
<body style=""font-family:Arial,sans-serif;margin:0;padding:20px;background:#f5f5f5;"">
<div style=""max-width:600px;margin:0 auto;background:#fff;padding:30px;border-radius:8px;"">
<h2 style=""color:#333;margin:0 0 20px;"">Verify Your Email</h2>
<p>Hello {username},</p>
<p>Thank you for registering. Click the button below to verify your email address (expires in 20 minutes).</p>
<p><a href=""{verifyUrl}"" style=""display:inline-block;background:#1976d2;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px;"">Verify Email</a></p>
<p style=""color:#666;font-size:14px;"">Or copy this link: {verifyUrl}</p>
<p style=""color:#666;font-size:13px;"">If you did not create an account, please ignore this email.</p>
</div></body></html>";
        }

        await SendEmailAsync(to, subject, htmlBody);
    }
}
