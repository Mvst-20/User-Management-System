using System.Net;
using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Configuration;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services.Security;

/// <summary>
/// 消费登录告警队列并发送邮件提醒。
/// <para>
/// 与登录主流程完全解耦：主流程只做一次非阻塞入队，邮件发送失败、SMTP 未配置
/// 都不会影响用户登录。同一用户 + 同一风险 + 同一设备/地点在
/// <c>Security:AlertDedupHours</c> 内只提醒一次。
/// </para>
/// </summary>
public class LoginAlertBackgroundService : BackgroundService
{
    private readonly ILoginAlertQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppConfiguration _appConfig;
    private readonly ILogger<LoginAlertBackgroundService> _logger;

    public LoginAlertBackgroundService(
        ILoginAlertQueue queue,
        IServiceScopeFactory scopeFactory,
        AppConfiguration appConfig,
        ILogger<LoginAlertBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _appConfig = appConfig;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await HandleAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送登录提醒失败：user={UserId}", message.UserId);
            }
        }
    }

    private async Task HandleAsync(LoginAlertMessage message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.Email))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var riskLabel = message.RiskKinds.Contains(LoginRiskKinds.NewLocation)
            ? "异地登录"
            : message.RiskKinds.Contains(LoginRiskKinds.NewNetwork)
                ? "陌生网络登录"
                : "新设备登录";

        // 去重：同用户 + 同风险 + 同设备/地点在窗口期内只发一次
        var dedupCutoff = DateTime.UtcNow.AddHours(-Math.Max(1, _appConfig.Security.AlertDedupHours));
        var alreadySent = await db.LoginLogs
            .AsNoTracking()
            .AnyAsync(l => l.UserId == message.UserId &&
                           l.EventType == LoginLogEvents.Alert &&
                           l.FailReason == riskLabel &&
                           l.DeviceName == message.DeviceName &&
                           l.Location == message.Location &&
                           l.CreatedAt >= dedupCutoff, ct);

        if (alreadySent)
        {
            return;
        }

        await emailService.SendEmailAsync(
            message.Email,
            $"[安全提醒] 你的账号有一次{riskLabel}",
            BuildHtml(message, riskLabel));

        db.LoginLogs.Add(new LoginLog
        {
            UserId = message.UserId,
            EventType = LoginLogEvents.Alert,
            Success = true,
            FailReason = riskLabel,
            Ip = message.Ip,
            Location = message.Location,
            DeviceName = message.DeviceName,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("已发送登录提醒：user={UserId} risk={Risk}", message.UserId, riskLabel);
    }

    private static string BuildHtml(LoginAlertMessage message, string riskLabel)
    {
        var time = message.TimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        var device = WebUtility.HtmlEncode(message.DeviceName);
        var ip = WebUtility.HtmlEncode(message.Ip ?? "未知");
        var location = WebUtility.HtmlEncode(message.Location ?? "未知");
        var username = WebUtility.HtmlEncode(message.Username);
        var timeCell = "padding:8px 0;font-family:'Fira Code',Consolas,monospace;";

        return $"""
<!DOCTYPE html>
<html lang="zh-CN">
<body style="margin:0;padding:24px;background:#F8FAFC;font-family:'Fira Sans','Segoe UI',Helvetica,Arial,sans-serif;color:#1E3A8A;">
  <div style="max-width:560px;margin:0 auto;background:#FFFFFF;border:1px solid #E2E8F0;border-radius:12px;overflow:hidden;">
    <div style="padding:20px 24px;background:linear-gradient(135deg,#1E40AF 0%,#3B82F6 100%);color:#FFFFFF;">
      <div style="font-size:13px;letter-spacing:.08em;opacity:.85;text-transform:uppercase;">账号安全</div>
      <div style="font-size:20px;font-weight:600;margin-top:6px;">{riskLabel}提醒</div>
    </div>
    <div style="padding:24px;">
      <p style="margin:0 0 16px;font-size:14px;line-height:1.7;">
        你好 <strong>{username}</strong>，你的账号刚刚在以下环境完成登录：
      </p>
      <table style="width:100%;border-collapse:collapse;font-size:13px;">
        <tr><td style="padding:8px 0;color:#64748B;width:96px;">时间</td><td style="{timeCell}">{time}</td></tr>
        <tr><td style="padding:8px 0;color:#64748B;">设备</td><td style="padding:8px 0;">{device}</td></tr>
        <tr><td style="padding:8px 0;color:#64748B;">IP</td><td style="{timeCell}">{ip}</td></tr>
        <tr><td style="padding:8px 0;color:#64748B;">归属地</td><td style="padding:8px 0;">{location}</td></tr>
      </table>
      <div style="margin-top:20px;padding:12px 16px;background:#FFFBEB;border-left:3px solid #F59E0B;border-radius:6px;font-size:13px;line-height:1.7;color:#92400E;">
        如果这不是你本人的操作，请立即登录系统，在「我的登录设备」中踢出该设备并修改密码。
      </div>
    </div>
    <div style="padding:14px 24px;background:#F8FAFC;border-top:1px solid #E2E8F0;font-size:12px;color:#64748B;">
      本邮件由系统自动发送，请勿直接回复。
    </div>
  </div>
</body>
</html>
""";
    }
}
