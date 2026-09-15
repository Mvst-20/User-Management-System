using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

/// <summary>
/// 服务器指标采集后台服务：
/// <para>
/// · 每 60s 调用 <see cref="ISystemMonitorService.GetSystemStatus"/> 采样一次并落库（server_metrics 表）；
/// · 每小时清理 8 天前的过期采样点，保证"至少保留一周"且表体积有界（60s × 8 天 ≈ 1.2 万行）。
/// </para>
/// 与服务器状态页面的 10s 轮询共享 <see cref="ISystemMonitorService"/> 单例差值状态：
/// 速率按真实时间窗计算、数值正确，仅采样窗口会因交错调用而抖动（已知可接受）。
/// </summary>
public sealed class MetricsCollectorService : BackgroundService
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(8);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private readonly ISystemMonitorService _monitor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MetricsCollectorService> _logger;

    public MetricsCollectorService(
        IServiceScopeFactory scopeFactory,
        ISystemMonitorService monitor,
        ILogger<MetricsCollectorService> logger)
    {
        _scopeFactory = scopeFactory;
        _monitor = monitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[METRICS] 服务器指标采集已启动（间隔 {Interval}，保留 {Days} 天）",
            SampleInterval, Retention.Days);

        // 启动即采样一次，避免图表前 1 分钟无数据
        await SampleSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(SampleInterval);
        var lastCleanup = DateTime.UtcNow;

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SampleSafelyAsync(stoppingToken);

                if (DateTime.UtcNow - lastCleanup >= CleanupInterval)
                {
                    await CleanupSafelyAsync(stoppingToken);
                    lastCleanup = DateTime.UtcNow;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 应用停止：正常结束
        }
    }

    private async Task SampleSafelyAsync(CancellationToken ct)
    {
        try
        {
            var status = _monitor.GetSystemStatus();
            var metric = new ServerMetric
            {
                RecordedAt = DateTime.UtcNow,
                CpuUsage = status.CpuUsage,
                MemoryUsage = status.MemoryUsage,
                MemoryUsedMb = Math.Round(status.MemoryUsedBytes / 1024.0 / 1024.0, 2),
                NetworkInBps = Math.Max(0, status.NetworkReceiveRate),
                NetworkOutBps = Math.Max(0, status.NetworkSendRate)
            };

            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<Data.ApplicationDbContext>();
            context.ServerMetrics.Add(metric);
            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 数据库暂不可用等：跳过本采样点，不影响宿主运行
            _logger.LogWarning(ex, "[METRICS] 指标采样落库失败，跳过本采样点");
        }
    }

    private async Task CleanupSafelyAsync(CancellationToken ct)
    {
        try
        {
            var cutoff = DateTime.UtcNow - Retention;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<Data.ApplicationDbContext>();
            var removed = await context.ServerMetrics
                .Where(m => m.RecordedAt < cutoff)
                .ExecuteDeleteAsync(ct);

            if (removed > 0)
            {
                _logger.LogInformation("[METRICS] 清理过期指标 {Count} 条（早于 {Cutoff:u}）", removed, cutoff);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[METRICS] 过期指标清理失败");
        }
    }
}
