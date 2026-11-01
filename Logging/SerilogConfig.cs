using Serilog;
using Serilog.Events;

namespace UserManagementSystem.Logging;

/// <summary>
/// Serilog 结构化日志配置。
/// Console: Information+ 带颜色; File: 按日滚动，分类输出 access / error / app 三个文件;
/// Memory: 环形缓冲最近 2000 条，供「运行日志」页面实时查看。
/// 注意：Program.cs 调用 UseSerilog() 时直接复用本静态 Logger，因此内存 Sink 只需注册在此一处。
/// </summary>
public static class SerilogConfig
{
    public static LoggerConfiguration CreateBootstrapLogger()
        => new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .WriteTo.Sink(MemoryLogSink.Instance)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: LogEventLevel.Information)
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(IsRequestCompletion)
                .WriteTo.File("logs/access-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level}] {Message:lj}{NewLine}"))
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => e.Level >= LogEventLevel.Error)
                .WriteTo.File("logs/error-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 90,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level}] {Message:lj}{NewLine}{Exception}"))
            .WriteTo.Logger(lc => lc
                .Filter.ByExcluding(e => IsRequestCompletion(e) || e.Level >= LogEventLevel.Error)
                .WriteTo.File("logs/app-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level}] {Message:lj}{NewLine}{Exception}"));

    /// <summary>
    /// 是否为「HTTP 请求完成」日志（Serilog.AspNetCore 的请求日志专用属性）。
    /// <para>
    /// 不能用 <c>RequestPath</c> 分类：ASP.NET Core 宿主会把 RequestPath 带进整个请求作用域，
    /// 导致请求内所有业务日志都被写进 access 文件、又被 app 文件排除。
    /// 只有请求完成事件才同时带 StatusCode 与 Elapsed。
    /// </para>
    /// </summary>
    private static bool IsRequestCompletion(LogEvent logEvent)
        => logEvent.Properties.ContainsKey("StatusCode") && logEvent.Properties.ContainsKey("Elapsed");
}
