using System.Globalization;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Options;
using Serilog;
using UserManagementSystem.Configuration;
using UserManagementSystem.Data;
using UserManagementSystem.Extensions;
using UserManagementSystem.Logging;
using UserManagementSystem.Services.Api;

// 兜底：任何在下方 try/catch 之外导致进程终止的异常，也要留下可见的诊断信息
AppDomain.CurrentDomain.UnhandledException += (_, args) =>
{
    if (args.ExceptionObject is Exception unhandled)
    {
        StartupFailureReporter.Report(unhandled, "AppDomain 未处理异常");
    }
};

// 全局区域：使 Ant Design 组件使用简体中文文案，并统一日期/数字格式
var culture = new CultureInfo("zh-CN");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

// ═══════════════════════════════════════════
//  引导日志（access / error / app 三文件分类，见 Logging/SerilogConfig）
// ═══════════════════════════════════════════
Log.Logger = SerilogConfig.CreateBootstrapLogger().CreateLogger();

Exception? startupFailure = null;

try
{
    Log.Information("═══════════════════════════════════════");
    Log.Information("  UMS (User Management System) starting");
    Log.Information("═══════════════════════════════════════");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // Kestrel 限制：请求体 10MB、请求头 30s、Keep-Alive 2min
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
        options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
        options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    });

    // ═══════════════════════════════════════════
    //  服务注册（详见 Extensions/ServiceCollectionExtensions）
    // ═══════════════════════════════════════════
    var appConfig = builder.AddAppConfiguration();
    builder.AddDatabase();
    builder.Services.AddJwtAuthentication(appConfig);
    builder.Services.AddAppServices();
    builder.AddAppBlazorUi(appConfig);
    builder.Services.AddAppSwagger();
    builder.Services.AddAppCors(appConfig, builder.Environment);

    var app = builder.Build();

    Log.Information("  UMS {Version} | Framework: ASP.NET Core 8.0", AppInfo.Version);

    // 数据库目标与来源（仅输出 server/database，不含任何凭据）
    Log.Information("  Database:  {Target}  via {Source}",
        DatabaseConnection.DescribeTarget(DatabaseConnection.Resolve(app.Configuration)),
        DatabaseConnection.DescribeSource());

    // 数据库迁移（含旧库基线兼容，见 Data/DatabaseMigrator）
    await DatabaseMigrator.MigrateAsync(app.Services);

    // 中间件管道 + 端点映射
    app.UseAppPipeline();
    app.MapAppEndpoints();

    // 监听地址在宿主启动完成后才最终确定（来源可能是 launchSettings 的 applicationUrl、--urls、
    // ASPNETCORE_URLS、HTTP_PORTS 或 IIS-ANCM 动态端口）。此处不能在 app.Run() 之前解析——那时
    // IServer 尚未监听，取不到任何地址，会回落到一个假地址并与真实端口不一致，导致照着日志访问
    // 却打不开页面，甚至让控制台的回环调用静默打到不存在的服务上。
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var selfBaseUrl = app.Services.GetRequiredService<SelfBaseUrlProvider>();

        // 解析失败（进程未报告任何 HTTP/HTTPS 监听地址）直接启动失败：
        // 由外层 catch + StartupFailureReporter 输出可读指引，绝不静默回落假地址。
        var baseUrl = selfBaseUrl.GetBaseUrl();

        // 回环调用的 Host 必须通过 HostFiltering。该合并只能在服务器启动后做（真实监听地址此刻才可见）：
        // 生产环境 AllowedHosts 通常只登记对外域名，若不并入回环地址，界面所有数据请求都会被 400 拒绝。
        // 必须取 IOptionsMonitor（而非 IOptions）：前者才是 HostFilteringMiddleware 持有的实例，
        // 两者各自缓存不同对象，改错对象不会生效（详见 SelfBaseUrlProvider.EnsureHostAllowed）。
        selfBaseUrl.EnsureHostAllowed(
            app.Services.GetRequiredService<IOptionsMonitor<HostFilteringOptions>>().CurrentValue);

        var address = baseUrl + "/";

        Log.Information("  ──────────────────────────────────────────────");
        Log.Information("  管理控制台: {Url}", address);
        Log.Information("  Swagger UI: {Url}swagger", address);
        Log.Information("  Health:     {Url}health", address);
        Log.Information("  回环基地址: {Url}  （取自进程真实监听地址，可用 AppSettings:SelfBaseUrl 覆盖）", baseUrl);
        Log.Information("  ──────────────────────────────────────────────");
    });

    app.Run();
}
catch (Exception ex)
{
    startupFailure = ex;
    Log.Fatal(ex, "UMS failed to start");
}
finally
{
    Log.Information("UMS shutting down");
    await Log.CloseAndFlushAsync();

    if (startupFailure is not null)
    {
        // Serilog 已落盘；这里再向标准错误输出并追加到 logs/startup-error.log，
        // 且交互式运行时等待按键 —— 避免"窗口一闪而过、完全看不到失败原因"
        StartupFailureReporter.Report(startupFailure);
    }
}

public partial class Program { }
