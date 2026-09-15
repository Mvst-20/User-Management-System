namespace UserManagementSystem.Services;

public interface ISystemMonitorService
{
    SystemStatus GetSystemStatus();
}

public record SystemStatus(
    TimeSpan Uptime,
    double CpuUsage,
    double MemoryUsage,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    long NetworkBytesSent,
    long NetworkBytesReceived,
    double NetworkSendRate,
    double NetworkReceiveRate
);
