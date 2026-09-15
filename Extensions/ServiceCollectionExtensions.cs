using System.Text;
using AntDesign;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using UserManagementSystem.Auth;
using UserManagementSystem.Configuration;
using UserManagementSystem.Data;
using UserManagementSystem.Logging;
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

        if (string.IsNullOrWhiteSpace(appConfig.Jwt.Key) || appConfig.Jwt.Key.Length < 64)
            throw new InvalidOperationException("JWT Key must be at least 64 characters.");

        // 注意：SMTP 配置完全由数据库维护（管理后台「邮件服务」页面），
        // 配置文件中已不再包含 Smtp 段落，因此此处不做任何校验与导入。

        builder.Services.AddSingleton(appConfig);
        AddAppDataProtection(builder, appConfig);
        AllowSelfCallHost(builder, appConfig);
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

        // 回环调用基地址按当前请求解析（见 SelfBaseUrlProvider），需访问 HttpContext
        services.AddHttpContextAccessor();
        services.AddScoped<SelfBaseUrlProvider>();

        // 前端运行于服务端，通过回环 HTTP 调用本进程 API：
        // 复用既有端点鉴权（RequirePermission），且无需 CORS。
        // 不设置 BaseAddress —— 绝对地址由 SelfBaseUrlProvider 按请求解析。
        services.AddHttpClient<UcsApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
    }

    /// <summary>
    /// 允许管理前端的回环自调用地址通过 Host 过滤。
    /// <para>
    /// 生产环境的 AllowedHosts 通常只登记对外域名（如 api.example.com），而管理前端会以本机地址
    /// 回环调用自身 API。该 Host 若未登记，将被 HostFiltering 直接以 400 Invalid Hostname 拒绝，
    /// 导致界面所有数据请求失败（表现为登录按钮无反应或"响应解析失败"）。
    /// </para>
    /// </summary>
    private static void AllowSelfCallHost(WebApplicationBuilder builder, AppConfiguration appConfig)
    {
        var selfBase = string.IsNullOrWhiteSpace(appConfig.AppSettings.SelfBaseUrl)
            ? SelfBaseUrlProvider.DeriveFromListenUrls(builder.Configuration)
            : appConfig.AppSettings.SelfBaseUrl;

        if (!Uri.TryCreate(SelfBaseUrlProvider.Normalize(selfBase), UriKind.Absolute, out var uri))
        {
            return;
        }

        builder.Services.PostConfigure<HostFilteringOptions>(options =>
        {
            var allowed = options.AllowedHosts;
            if (allowed is null || allowed.Count == 0 || allowed.Any(host => host == "*"))
            {
                return;
            }

            // 注意：框架从配置读取 AllowedHosts 时使用 Split(';')，得到的是定长集合，
            // 因此必须重新赋值而不能就地 Add。
            var merged = new List<string>(allowed);
            foreach (var candidate in new[] { uri.Authority, uri.Host })
            {
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    !merged.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    merged.Add(candidate);
                }
            }

            options.AllowedHosts = merged;
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

        services.AddScoped<IOnlineUserService, OnlineUserService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddSingleton<IJwtService, JwtService>();
        services.AddScoped<ITokenBlacklistService, TokenBlacklistService>();
        services.AddScoped<IRateLimitService, RateLimitService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IUserGroupService, UserGroupService>();
        services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
        services.AddScoped<IFollowService, FollowService>();
        services.AddScoped<ISystemSettingService, SystemSettingService>();
        services.AddScoped<ISmtpConfigService, SmtpConfigService>();
        services.AddScoped<ISubsystemService, SubsystemService>();

        services.AddHostedService<InitializationService>();
        services.AddHostedService<TokenCleanupService>();
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

                    var userIdClaim = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    if (userIdClaim != null && ulong.TryParse(userIdClaim, out var userId))
                    {
                        // 用户状态缓存 30s，避免每个请求都查询 users 表；
                        // 状态变更时由 UserService 主动清除对应缓存
                        var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                        var cacheKey = UserService.UserStatusCacheKey(userId);
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
