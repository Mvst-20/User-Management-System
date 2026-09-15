namespace UserManagementSystem.Models;

/// <summary>
/// 服务器指标采样点。
/// 由 <see cref="Services.MetricsCollectorService"/> 按 60s 定时落库，保留 8 天
/// （满足"服务器状态数据至少持久记录一周"）。
/// </summary>
public class ServerMetric
{
    public ulong Id { get; set; }

    /// <summary>采样时间（UTC）。</summary>
    public DateTime RecordedAt { get; set; }

    /// <summary>CPU 使用率（0~100）。</summary>
    public double CpuUsage { get; set; }

    /// <summary>内存占用率（0~100）。</summary>
    public double MemoryUsage { get; set; }

    /// <summary>已用内存（MB）。</summary>
    public double MemoryUsedMb { get; set; }

    /// <summary>网络接收速率（bytes/s）。</summary>
    public double NetworkInBps { get; set; }

    /// <summary>网络发送速率（bytes/s）。</summary>
    public double NetworkOutBps { get; set; }
}
