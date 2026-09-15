using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace UserManagementSystem.Services;

public class SystemMonitorService : ISystemMonitorService
{
    private readonly ILogger<SystemMonitorService> _logger;
    
    // 网络采样数据
    private long _lastNetworkBytesSent;
    private long _lastNetworkBytesReceived;
    private DateTime _lastNetworkSampleTime = DateTime.UtcNow;
    private double _networkSendRate;
    private double _networkReceiveRate;
    private readonly object _networkLock = new();

    // CPU 采样数据（系统级）
    private DateTime _lastCpuSampleTime = DateTime.UtcNow;
    private double _cpuUsage;
    private readonly object _cpuLock = new();

    // Windows/Linux 系统 CPU 时间
    private long _lastKernelTime;
    private long _lastUserTime;
    private long _lastIdleTime;

    // 进程 CPU 时间（回退方案）
    private TimeSpan _lastCpuTotalTime;

    public SystemMonitorService(ILogger<SystemMonitorService> logger)
    {
        _logger = logger;
        
        InitializeSampling();
    }

    private void InitializeSampling()
    {
        try
        {
            var networkStats = GetNetworkStats();
            _lastNetworkBytesSent = networkStats.BytesSent;
            _lastNetworkBytesReceived = networkStats.BytesReceived;
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                InitializeWindowsCpuSampling();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                InitializeLinuxCpuSampling();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize system monitoring sampling");
        }
    }

    private void InitializeWindowsCpuSampling()
    {
        try
        {
            if (GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            {
                _lastIdleTime = idleTime;
                _lastKernelTime = kernelTime;
                _lastUserTime = userTime;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Windows CPU sampling");
        }
    }

    private void InitializeLinuxCpuSampling()
    {
        try
        {
            var (idle, total) = GetLinuxCpuTimes();
            _lastIdleTime = idle;
            _lastKernelTime = total; // 重用字段存储 total
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Linux CPU sampling");
        }
    }

    public SystemStatus GetSystemStatus()
    {
        var uptime = GetSystemUptime();
        var cpuUsage = GetCpuUsage();
        var (memoryUsed, memoryTotal, memoryUsage) = GetMemoryInfo();
        var (bytesSent, bytesReceived, sendRate, receiveRate) = GetNetworkInfo();

        return new SystemStatus(
            uptime,
            cpuUsage,
            memoryUsage,
            memoryUsed,
            memoryTotal,
            bytesSent,
            bytesReceived,
            sendRate,
            receiveRate
        );
    }

    private TimeSpan GetSystemUptime()
    {
        try
        {
            var tickCount = Environment.TickCount64;
            if (tickCount <= 0) throw new InvalidOperationException("TickCount64 returned zero");
            return TimeSpan.FromMilliseconds(tickCount);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TickCount64 unavailable, falling back to process start time");
            var process = Process.GetCurrentProcess();
            return DateTime.UtcNow - process.StartTime.ToUniversalTime();
        }
    }

    private double GetCpuUsage()
    {
        lock (_cpuLock)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return GetWindowsCpuUsage();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return GetLinuxCpuUsage();
                }
                else
                {
                    // macOS 或其他平台 - 回退到进程级
                    return GetProcessCpuUsage();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get CPU usage");
                return 0;
            }
        }
    }

    private double GetWindowsCpuUsage()
    {
        var result = GetSystemTimes(out var idleTime, out var kernelTime, out var userTime);

        if (!result)
        {
            _logger.LogDebug("GetSystemTimes failed (code {Error}), reusing previous CPU value",
                Marshal.GetLastWin32Error());
            return _cpuUsage;
        }

        var idleDelta = idleTime - _lastIdleTime;
        var kernelDelta = kernelTime - _lastKernelTime;
        var userDelta = userTime - _lastUserTime;
        var totalDelta = kernelDelta + userDelta;

        if (totalDelta > 0)
        {
            _cpuUsage = (1.0 - (double)idleDelta / totalDelta) * 100.0;
            _cpuUsage = Math.Max(0, Math.Min(100, _cpuUsage));
        }

        _lastIdleTime = idleTime;
        _lastKernelTime = kernelTime;
        _lastUserTime = userTime;
        _lastCpuSampleTime = DateTime.UtcNow;

        return Math.Round(_cpuUsage, 2);
    }

    private double GetLinuxCpuUsage()
    {
        var (idle, total) = GetLinuxCpuTimes();

        var idleDelta = idle - _lastIdleTime;
        var totalDelta = total - _lastKernelTime; // 重用字段存储 total

        if (totalDelta > 0)
        {
            _cpuUsage = (1.0 - (double)idleDelta / totalDelta) * 100.0;
            _cpuUsage = Math.Max(0, Math.Min(100, _cpuUsage));
        }

        _lastIdleTime = idle;
        _lastKernelTime = total;
        _lastCpuSampleTime = DateTime.UtcNow;

        return Math.Round(_cpuUsage, 2);
    }

    private double GetProcessCpuUsage()
    {
        var now = DateTime.UtcNow;
        var process = Process.GetCurrentProcess();
        var currentCpuTime = process.TotalProcessorTime;

        var cpuUsedMs = (currentCpuTime - _lastCpuTotalTime).TotalMilliseconds;
        var elapsedMs = (now - _lastCpuSampleTime).TotalMilliseconds;

        if (elapsedMs > 0)
        {
            _cpuUsage = (cpuUsedMs / elapsedMs / Environment.ProcessorCount) * 100.0;
            _cpuUsage = Math.Max(0, Math.Min(100, _cpuUsage));
        }

        _lastCpuTotalTime = currentCpuTime;
        _lastCpuSampleTime = now;

        return Math.Round(_cpuUsage, 2);
    }

    private (long idle, long total) GetLinuxCpuTimes()
    {
        try
        {
            var line = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu "));
            if (line != null)
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 6)
                {
                    // user, nice, system, idle, iowait
                    long.TryParse(parts[1], out var user);
                    long.TryParse(parts[2], out var nice);
                    long.TryParse(parts[3], out var system);
                    long.TryParse(parts[4], out var idle);
                    long.TryParse(parts[5], out var iowait);

                    var total = user + nice + system + idle + iowait;
                    return (idle, total);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read /proc/stat");
        }

        return (_lastIdleTime, _lastKernelTime);
    }

    private (long used, long total, double percent) GetMemoryInfo()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetWindowsMemoryInfo();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return GetLinuxMemoryInfo();
            }
            else
            {
                // macOS 或其他平台 - 使用 GC 信息作为回退
                var gcInfo = GC.GetGCMemoryInfo();
                var totalMem = gcInfo.TotalAvailableMemoryBytes;
                var usedMem = Environment.WorkingSet;
                var percent = totalMem > 0 ? Math.Round((double)usedMem / totalMem * 100, 2) : 0;
                return (usedMem, totalMem, percent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get memory info");
            return (0, 0, 0);
        }
    }

    private (long used, long total, double percent) GetWindowsMemoryInfo()
    {
        try
        {
            var memInfo = new MEMORYSTATUSEX();
            var result = GlobalMemoryStatusEx(memInfo);

            if (result)
            {
                var totalMem = (long)memInfo.ullTotalPhys;
                var availMem = (long)memInfo.ullAvailPhys;
                var usedMem = totalMem - availMem;
                var percent = (double)memInfo.dwMemoryLoad;
                return (usedMem, totalMem, Math.Round(percent, 2));
            }
            else
            {
                _logger.LogDebug("GlobalMemoryStatusEx failed (code {Error}), using fallback", Marshal.GetLastWin32Error());
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GlobalMemoryStatusEx unavailable, using process memory fallback");
        }

        // Fallback: process-level memory (GC info)
        var gcInfo = GC.GetGCMemoryInfo();
        var fallbackTotal = gcInfo.TotalAvailableMemoryBytes;
        var fallbackUsed = Environment.WorkingSet;
        var fallbackPercent = fallbackTotal > 0 ? Math.Round((double)fallbackUsed / fallbackTotal * 100, 2) : 0;
        return (fallbackUsed, fallbackTotal, fallbackPercent);
    }

    private (long used, long total, double percent) GetLinuxMemoryInfo()
    {
        try
        {
            var lines = File.ReadAllLines("/proc/meminfo");
            long totalKb = 0, availableKb = 0;

            foreach (var line in lines)
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[1], out var value))
                {
                    if (line.StartsWith("MemTotal:"))
                        totalKb = value;
                    else if (line.StartsWith("MemAvailable:"))
                        availableKb = value;

                    if (totalKb > 0 && availableKb > 0)
                        break;
                }
            }

            if (totalKb > 0 && availableKb > 0)
            {
                var totalBytes = totalKb * 1024;
                var usedBytes = (totalKb - availableKb) * 1024;
                var percent = Math.Round((double)usedBytes / totalBytes * 100, 2);
                return (usedBytes, totalBytes, percent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Linux memory info from /proc/meminfo");
        }

        // 回退方案
        var gcInfo = GC.GetGCMemoryInfo();
        var fallbackTotal = gcInfo.TotalAvailableMemoryBytes;
        var fallbackUsed = Environment.WorkingSet;
        var fallbackPercent = fallbackTotal > 0 ? Math.Round((double)fallbackUsed / fallbackTotal * 100, 2) : 0;
        return (fallbackUsed, fallbackTotal, fallbackPercent);
    }

    private (long bytesSent, long bytesReceived, double sendRate, double receiveRate) GetNetworkInfo()
    {
        lock (_networkLock)
        {
            try
            {
                var now = DateTime.UtcNow;
                var stats = GetNetworkStats();
                var elapsedSeconds = (now - _lastNetworkSampleTime).TotalSeconds;

                if (elapsedSeconds > 0)
                {
                    _networkSendRate = (stats.BytesSent - _lastNetworkBytesSent) / elapsedSeconds;
                    _networkReceiveRate = (stats.BytesReceived - _lastNetworkBytesReceived) / elapsedSeconds;
                }

                _lastNetworkBytesSent = stats.BytesSent;
                _lastNetworkBytesReceived = stats.BytesReceived;
                _lastNetworkSampleTime = now;

                return (
                    stats.BytesSent,
                    stats.BytesReceived,
                    Math.Round(_networkSendRate, 2),
                    Math.Round(_networkReceiveRate, 2)
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get network info");
                return (0, 0, 0, 0);
            }
        }
    }

    private (long BytesSent, long BytesReceived) GetNetworkStats()
    {
        long totalSent = 0;
        long totalReceived = 0;

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                // 排除回环接口和非活动接口
                if (nic.OperationalStatus == OperationalStatus.Up &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var stats = nic.GetIPv4Statistics();
                    totalSent += stats.BytesSent;
                    totalReceived += stats.BytesReceived;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get network interface stats");
        }

        return (totalSent, totalReceived);
    }

    // Windows API 结构体和函数
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out long lpIdleTime,
        out long lpKernelTime,
        out long lpUserTime);
}
