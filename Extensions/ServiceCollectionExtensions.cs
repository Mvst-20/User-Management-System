using System.Text;
using AntDesign;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using UserManagementSystem.Auth;
using UserManagementSystem.Configuration;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Logging;
using UserManagementSystem.Services.Security;
using UserManagementSystem.Models;
using UserManagementSystem.Services;
using UserManagementSystem.Services.Api;

namespace UserManagementSystem.Extensions;

/// <summary>
/// 应用服务注册：按职责分组，由 Program.cs 顺序调用。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 绑定强类型配置并执行启动校验（配置缺失/非法时快速失败）。
    /// </summary>
    public static AppConfiguration AddAppConfiguration(this WebApplicationBuilder builder)
    {
        var appConfig = new AppConfiguration();
        builder.Configuration.GetSection("Jwt").Bind(appConfig.Jwt);
        builder.Configuration.GetSection("AppSettings").Bind(appConfig.AppSettings);
        builder.Configuration.GetSection("Permissions").Bind(appConfig.Permissions);
        builder.Configuration.GetSection("Security").Bind(appConfig.Security);

        if (string.IsNullOrWhiteSpace(appConfig.Jwt.Key) || appConfig.Jwt.Key.Length < 64)
            throw new InvalidOperationException("JWT Key must be at least 64 characters.");

        // 注意：SMTP 配置完全由数据库维护（管理后台「邮件服务」页面），
        // 配置文件中已不再包含 Smtp 段落，因此此处不做任何校验与导入。

        builder.Services.AddSingleton(appConfig);
        AddAppDataProtection(builder, appConfig);
        return appConfig;
    }

    /// <summary>
    /// 注册 Data Protection 并将密钥环持久化到磁盘目录。
    /// SMTP 密码以该密钥环加密存储：若密钥环丢失（未持久化、容器重建等），
    /// 已存密码将无法解密，此时服务降级为"未配置"并提示管理员重新录入。
    /// </summary>
    private static void AddAppDataProtection(WebApplicationBuilder builder, AppConfiguration appConfig)
    {
        var configured = appConfig.AppSettings.DataProtectionKeysPath;
        var keysPath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(builder.Environment.ContentRootPath, "keys")
            : Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(builder.Environment.ContentRootPath, configured);

        Directory.CreateDirectory(keysPath);

        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .SetApplicationName("UCS");
    }

    /// <summary>
    /// 注册 Blazor 管理前端（Interactive Server）、Ant Design 组件库、
    /// 前端会话/权限状态，以及回环调用本进程 REST API 的客户端。
    /// </summary>
    public static void AddAppBlazorUi(this WebApplicationBuilder builder, AppConfiguration appConfig)
    {
        var services = builder.Services;

        services.AddRazorComponents()
            .AddInteractiveServerComponents(options =>
                options.DetailedErrors = builder.Environment.IsDevelopment());
        services.AddAntDesign();
        services.AddCascadingAuthenticationState();

        // 前端会话与权限状态：每个 circuit 一份
        services.AddScoped<TokenStore>();
        services.AddScoped<SessionPersistence>();
        services.AddScoped<PermissionState>();
        // 页面标题/操作区提升到顶栏的桥接状态（PageContainer 写、TopBar 读）
        services.AddScoped<UserManagementSystem.Components.Shared.PageHeaderService>();
        // 覆盖默认提供者：认证状态来自 TokenStore（JWT），而非 Cookie
        services.AddScoped<AuthenticationStateProvider, JwtAuthenticationStateProvider>();

        // circuit 建立/关闭记录：用于区分"前端交互通道未建立"与"应用逻辑异常"
        services.AddScoped<CircuitHandler, LoggingCircuitHandler>();

        // 回环调用基地址取自进程真实监听地址（见 SelfBaseUrlProvider）。
        // 单例：地址在启动后即固定，进程内缓存一次即可，保证启动横幅、
        // HostFiltering 白名单与回环调用三处使用的是同一个地址。
        services.AddSingleton<SelfBaseUrlProvider>();

        // 前端运行于服务端，通过回环 HTTP 调用本进程 API：
        // 复用既有端点鉴权（RequirePermission），且无需 CORS。
        // 不设置 BaseAddress —— 绝对地址由 SelfBaseUrlProvider 解析（取自真实监听地址）。
        services.AddHttpClient<UcsApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
    }


    /// <summary>
    /// 注册数据库上下文（MySQL，固定 ServerVersion + 失败重试）。
    /// 连接串来自系统（环境）变量，见 <see cref="DatabaseConnection"/>。
    /// </summary>
    public static void AddDatabase(this WebApplicationBuilder builder)
    {
        // 启动即校验：缺失或明显非法时以明确的指引快速失败，而不是在首次访问数据库时报出难以理解的异常
        var connectionString = DatabaseConnection.Resolve(builder.Configuration);

        builder.Services.AddDbContext<ApplicationDbContext>(options =>
        {
            // 固定 MySqlServerVersion：避免 AutoDetect 在启动/设计时额外发起探测连接
            options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 21)), mySqlOptions =>
            {
                mySqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
            });
            if (builder.Environment.IsDevelopment())
                options.EnableSensitiveDataLogging();
        });
    }

    /// <summary>
    /// 注册应用业务服务与后台服务。
    /// 基于 DbContext 的服务统一 Scoped；无状态服务 Singleton。
    /// </summary>
    public static void AddAppServices(this IServiceCollection services)
    {
        services.AddMemoryCache();

        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddSingleton<IJwtService, JwtService>();
        services.AddScoped<ITokenBlacklistService, TokenBlacklistService>();
        services.AddScoped<IRateLimitService, RateLimitService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<ICacheVersionService, CacheVersionService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IUserGroupService, UserGroupService>();
        services.AddSingleton<IIpLocationResolver, Ip2RegionLocationResolver>();
        services.AddScoped<ILoginRiskEvaluator, LoginRiskEvaluator>();
        services.AddSingleton<ILoginAlertQueue, LoginAlertQueue>();
        services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
        services.AddScoped<ISystemSettingService, SystemSettingService>();
        services.AddScoped<ISmtpConfigService, SmtpConfigService>();
        services.AddScoped<ISubsystemService, SubsystemService>();

        services.AddHostedService<InitializationService>();
        services.AddHostedService<TokenCleanupService>();
        // 登录风险提醒的异步发送（与登录主流程解耦）
        services.AddHostedService<LoginAlertBackgroundService>();
        // 服务器指标定时采集（60s 落库，保留 8 天）；置于初始化服务之后，启动时迁移已完成
        services.AddHostedService<MetricsCollectorService>();
    }

    /// <summary>
    /// 注册 JWT Bearer 认证：验签参数 + Token 黑名单检查 + 用户状态检查。
    /// </summary>
    public static void AddJwtAuthentication(this IServiceCollection services, AppConfiguration appConfig)
    {
        services.AddAuthentication(options =>
        {
            // 默认使用多认证分派方案：Authorization: Bearer → JWT；X-Api-Key → 子系统凭证。
            // JWT 方案名保持 "Bearer" 不变，既有调用方行为逐位一致。
            options.DefaultScheme = AuthSchemes.Multi;
            options.DefaultAuthenticateScheme = AuthSchemes.Multi;
            options.DefaultChallengeScheme = AuthSchemes.Multi;
        })
        .AddJwtBearer(AuthSchemes.Jwt, options =>
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
                        if (await blacklistService.IsBlacklistedAsync(jti))
                        {
                            context.Fail("Token has been revoked");
                            return;
                        }
                    }

                    // 会话校验：JWT 中的 sid 指向 user_sessions。
                    // 撤销会话（踢下线、被新设备顶替、封禁）后，最迟 SessionValidationCacheSeconds
                    // 内所有实例都会拒绝该 token。旧版本令牌（无 sid）放行，兼容 ≤ExpiryMinutes 的过渡窗口。
                    var sessionId = context.Principal?.FindFirst(JwtClaimNames.SessionId)?.Value;
                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        var sessionService = context.HttpContext.RequestServices.GetRequiredService<ISessionService>();
                        if (!await sessionService.IsSessionActiveAsync(sessionId))
                        {
                            context.Fail("Session has been revoked");
                            return;
                        }

                        // 活跃时间节流上报（同会话 1 分钟内只写一次），供「已登录客户端」排序与在线统计
                        await sessionService.TouchAsync(
                            sessionId,
                            context.HttpContext.Connection.RemoteIpAddress?.ToString());
                    }

                    var userIdClaim = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    if (userIdClaim != null && ulong.TryParse(userIdClaim, out var userId))
                    {
                        // 用户状态缓存 30s，避免每个请求都查询 users 表；
                        // 键内嵌 user_status 版本号：状态变更（封禁/解封/删除）时版本号递增，
                        // 所有实例的旧键立即失效，不再有 30 秒的滞后窗口。
                        var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                        var versionService = context.HttpContext.RequestServices.GetRequiredService<ICacheVersionService>();
                        var statusVersion = await versionService.GetVersionAsync(CacheScopes.UserStatus);
                        var cacheKey = UserService.UserStatusCacheKey(statusVersion, userId);
                        if (!cache.TryGetValue(cacheKey, out UserStatus status))
                        {
                            var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
                            var user = await userService.GetByIdAsync(userId);
                            if (user == null)
                            {
                                context.Fail("User account is not active");
                                return;
                            }
                            status = user.Status;
                            cache.Set(cacheKey, status, TimeSpan.FromSeconds(30));
                        }

                        if (status == UserStatus.Deleted || status == UserStatus.Banned)
                        {
                            context.Fail("User account is not active");
                        }
                    }
                }
            };
        })
        // 子系统 API Key 凭证方案（与用户 JWT 并存）
        .AddScheme<AuthenticationSchemeOptions, SubsystemKeyAuthenticationHandler>(AuthSchemes.SubsystemKey, _ => { })
        .AddPolicyScheme(AuthSchemes.Multi, AuthSchemes.Multi, options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                if (context.Request.Headers.ContainsKey("Authorization"))
                    return AuthSchemes.Jwt;

                if (context.Request.Headers.ContainsKey(SubsystemKeyAuthenticationHandler.ApiKeyHeader))
                    return AuthSchemes.SubsystemKey;

                // 无任何凭证时交由 JWT 处理，保持既有的 401 行为
                return AuthSchemes.Jwt;
            };
        });

        services.AddAuthorization(options =>
        {
            // 子系统凭证专用策略：仅接受 SubsystemKey 方案。
            // 用户 JWT（Bearer 方案）不在该策略的允许方案内，即使携带有效令牌也无法通过，
            // 保证子系统自注册端点只能被子系统凭证调用。
            options.AddPolicy(AuthPolicies.SubsystemKey, policy => policy
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthSchemes.SubsystemKey));
        });
    }

    /// <summary>
    /// 注册 Swagger（仅开发环境在管道中启用）。
    /// </summary>
    public static void AddAppSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
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
    }

    /// <summary>
    /// 注册 CORS：白名单来自 AppSettings.AllowedOrigins（逗号分隔）。
    /// 未配置时：开发环境放行所有来源，生产环境阻止所有跨域请求。
    /// </summary>
    public static void AddAppCors(this IServiceCollection services, AppConfiguration appConfig, IHostEnvironment environment)
    {
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                var allowedOrigins = appConfig.AppSettings.AllowedOrigins;
                if (!string.IsNullOrEmpty(allowedOrigins))
                {
                    var origins = allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    policy.WithOrigins(origins)
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                }
                else if (environment.IsDevelopment())
                {
                    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                }
                else
                {
                    policy.SetIsOriginAllowed(_ => false).AllowAnyMethod().AllowAnyHeader();
                }
            });
        });
    }
}
