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
                .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("RequestPath"))
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
                .Filter.ByExcluding(e => e.Properties.ContainsKey("RequestPath") || e.Level >= LogEventLevel.Error)
                .WriteTo.File("logs/app-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level}] {Message:lj}{NewLine}{Exception}"));
}
