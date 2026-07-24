using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using UserManagementSystem.Configuration;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Endpoints;
using UserManagementSystem.Middleware;
using UserManagementSystem.Services;

// ═══════════════════════════════════════════
//  Serilog 结构化日志配置
//  Console: Information+ 带颜色; File: Debug+ 按日滚动
//  分类输出: access / error / app 三个独立文件
// ═══════════════════════════════════════════
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
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
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level}] {Message:lj}{NewLine}{Exception}"))
    .CreateLogger();

try
{
    Log.Information("═══════════════════════════════════════");
    Log.Information("  UMS (User Management System) starting");
    Log.Information("═══════════════════════════════════════");

    var builder = WebApplication.CreateBuilder(args);

    // Replace built-in logging with Serilog
    builder.Host.UseSerilog();

    // ============ Kestrel Configuration ============
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
        options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
        options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    });

    // ============ Config Binding ============
    var appConfig = new AppConfiguration();
    builder.Configuration.GetSection("Jwt").Bind(appConfig.Jwt);
    builder.Configuration.GetSection("Smtp").Bind(appConfig.Smtp);
    builder.Configuration.GetSection("AppSettings").Bind(appConfig.AppSettings);

    // Startup validation
    if (string.IsNullOrWhiteSpace(appConfig.Jwt.Key) || appConfig.Jwt.Key.Length < 64)
        throw new InvalidOperationException("JWT Key must be at least 64 characters.");
    if (string.IsNullOrWhiteSpace(appConfig.Smtp.Host))
        throw new InvalidOperationException("SMTP Host is required.");
    if (string.IsNullOrWhiteSpace(appConfig.Smtp.Username) || string.IsNullOrWhiteSpace(appConfig.Smtp.Password))
        throw new InvalidOperationException("SMTP credentials are required.");
    if (string.IsNullOrWhiteSpace(appConfig.Smtp.From))
        throw new InvalidOperationException("SMTP From address is required.");

    // ============ Database ============
    var connectionString = builder.Configuration.GetConnectionString("Default");
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
    {
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), mySqlOptions =>
        {
            mySqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
        });
        if (builder.Environment.IsDevelopment())
            options.EnableSensitiveDataLogging();
    });

    // ============ Service Registration ============
    builder.Services.AddSingleton(appConfig);
    builder.Services.AddMemoryCache();

    builder.Services.AddSingleton<IOnlineUserService, OnlineUserService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<ITokenService, TokenService>();
    builder.Services.AddScoped<IEmailService, EmailService>();
    builder.Services.AddSingleton<IJwtService, JwtService>();
    builder.Services.AddSingleton<ITokenBlacklistService, TokenBlacklistService>();
    builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
    builder.Services.AddScoped<IPermissionService, PermissionService>();
    builder.Services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
    builder.Services.AddScoped<IFollowService, FollowService>();
    builder.Services.AddScoped<ISystemSettingService, SystemSettingService>();

    // Background services
    builder.Services.AddHostedService<InitializationService>();
    builder.Services.AddHostedService<TokenCleanupService>();

    // ============ JWT Auth ============
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = appConfig.Jwt.Issuer,
            ValidAudience = appConfig.Jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(appConfig.Jwt.Key)),
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var jti = context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
                if (jti != null)
                {
                    var blacklistService = context.HttpContext.RequestServices.GetRequiredService<ITokenBlacklistService>();
                    if (blacklistService.IsBlacklisted(jti))
                    {
                        context.Fail("Token has been revoked");
                        return;
                    }
                }

                var userIdClaim = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (userIdClaim != null && ulong.TryParse(userIdClaim, out var userId))
                {
                    var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
                    var user = await userService.GetByIdAsync(userId);
                    if (user == null || user.Status == UserManagementSystem.Models.UserStatus.Deleted
                        || user.Status == UserManagementSystem.Models.UserStatus.Banned)
                    {
                        context.Fail("User account is not active");
                    }
                }
            }
        };
    });

    builder.Services.AddAuthorization();

    // ============ Swagger ============
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "User Management API",
            Version = "v1",
            Description = "User Management System API"
        });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer"
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            }
        });
    });

    // ============ CORS ============
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            // 读配置：逗号分隔白名单（如 "https://32px.top,https://bwbt.top"）
            var allowedOrigins = appConfig.AppSettings.AllowedOrigins;
            if (!string.IsNullOrEmpty(allowedOrigins))
            {
                var origins = allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                policy.WithOrigins(origins)
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            }
            else if (builder.Environment.IsDevelopment())
            {
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
            }
            else
            {
                policy.SetIsOriginAllowed(_ => false).AllowAnyMethod().AllowAnyHeader();
            }
        });
    });

    var app = builder.Build();

    // ============ Banner ============
    Log.Information("  UMS {Version} | Framework: ASP.NET Core 8.0", "v1.0.0");

    // ============ Database Init ============
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var canConnect = await context.Database.CanConnectAsync();

        bool tablesExist = false;
        if (canConnect)
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync("SELECT 1 FROM users LIMIT 1");
                tablesExist = true;
            }
            catch
            {
                tablesExist = false;
            }
        }

        if (!canConnect || !tablesExist)
        {
            Log.Information("Database: creating tables...");
            await context.Database.EnsureCreatedAsync();
        }
    }

    // ============ Middleware Pipeline ============
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.000}ms";
        options.GetLevel = (ctx, elapsed, ex) =>
            ex != null ? LogEventLevel.Error
            : ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
            : ctx.Response.StatusCode >= 400 ? LogEventLevel.Warning
            : LogEventLevel.Information;
    });

    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // Ensure directories exist
    Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars"));
    Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "logs"));

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseStaticFiles();
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();
    // 注意：UMS 使用 JWT Bearer Token 认证，不使用 Cookie，
    // 因此不需要 antiforgery 中间件。
    // 各端点的 .DisableAntiforgery() 标注可保留作为文档说明，不影响运行。

    // ============ Endpoint Mapping ============
    app.MapUserEndpoints();
    app.MapAvatarEndpoints();
    app.MapAdminEndpoints();
    app.MapPermissionEndpoints();
    app.MapUserGroupEndpoints();
    app.MapFollowEndpoints();
    app.MapSystemSettingEndpoints();

    app.MapGet("/health", () => Results.Ok(new ApiResponse(ResultCodes.Success, "healthy",
        new { status = "healthy", timestamp = DateTime.UtcNow })));

    var urls = app.Urls.Any() ? app.Urls.First() : "http://localhost:5000";
    var displayUrl = urls.Contains("0.0.0.0") ? "http://localhost:5000/" : urls.TrimEnd('/') + "/";

    Log.Information("  Server started: {Url}", displayUrl);
    Log.Information("  Swagger UI: {Url}swagger", displayUrl);
    Log.Information("  Health:     {Url}health", displayUrl);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "UMS failed to start");
}
finally
{
    Log.Information("UMS shutting down");
    await Log.CloseAndFlushAsync();
}

public partial class Program { }
