using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
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
    /// <summary>标记本次认证失败的原因是“认证状态不可用”（数据库故障/超时），用于返回 503 而不是 401。</summary>
    private const string AuthStateUnavailableItemKey = "ucs.auth.state_unavailable";

    /// <summary>认证状态不可用的告警节流：数据库长时间故障时避免每个请求写一条日志。</summary>
    private static long _lastAuthStateFailureLogTicks;

    private static bool ShouldLogAuthStateFailure()
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastAuthStateFailureLogTicks);
        if (now - last < TimeSpan.FromSeconds(30).Ticks)
        {
            return false;
        }

        return Interlocked.CompareExchange(ref _lastAuthStateFailureLogTicks, now, last) == last;
    }

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

        if (string.IsNullOrWhiteSpace(appConfig.Jwt.Issuer) || string.IsNullOrWhiteSpace(appConfig.Jwt.Audience))
            throw new InvalidOperationException("Jwt:Issuer 与 Jwt:Audience 必须配置。");

        // 签名密钥绝不入库：优先外部注入（Jwt__Key），缺失时按配置决定“启动失败”或“生成到 keys/”。
        // 注意：密钥一旦轮换，所有已签发的 Access Token 立即失效。
        var keysDirectory = ResolveKeysDirectory(builder, appConfig);
        appConfig.Jwt.Key = JwtKeyProvider.Resolve(appConfig.Jwt, keysDirectory, out var keyWarning);
        if (!string.IsNullOrWhiteSpace(keyWarning))
        {
            Serilog.Log.Warning("{Warning}", keyWarning);
        }

        // 注意：SMTP 配置完全由数据库维护（管理后台「邮件服务」页面），
        // 配置文件中已不再包含 Smtp 段落，因此此处不做任何校验与导入。

        builder.Services.AddSingleton(appConfig);
        AddAppDataProtection(builder, appConfig);
        return appConfig;
    }

    /// <summary>解析 Data Protection / 本地 JWT 密钥目录（相对路径基于内容根目录）。</summary>
    private static string ResolveKeysDirectory(WebApplicationBuilder builder, AppConfiguration appConfig)
    {
        var configured = appConfig.AppSettings.DataProtectionKeysPath;
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(builder.Environment.ContentRootPath, "keys")
            : Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(builder.Environment.ContentRootPath, configured);
    }

    /// <summary>
    /// 注册 Data Protection 并将密钥环持久化到磁盘目录。
    /// SMTP 密码以该密钥环加密存储：若密钥环丢失（未持久化、容器重建等），
    /// 已存密码将无法解密，此时服务降级为"未配置"并提示管理员重新录入。
    /// </summary>
    private static void AddAppDataProtection(WebApplicationBuilder builder, AppConfiguration appConfig)
    {
        var keysPath = ResolveKeysDirectory(builder, appConfig);

        Directory.CreateDirectory(keysPath);

        var dataProtection = builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .SetApplicationName("UCS");

        // 静态加密密钥环：指定自定义文件系统存储后，密钥默认以明文 XML 落盘；
        // 生产环境应提供 PFX/P12 证书（口令通过环境变量注入），避免 keys/ 泄露即失守。
        var certificatePath = appConfig.AppSettings.DataProtectionCertificatePath;
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            var fullPath = Path.IsPathRooted(certificatePath)
                ? certificatePath
                : Path.Combine(builder.Environment.ContentRootPath, certificatePath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Data Protection 证书文件不存在：{fullPath}（检查 AppSettings:DataProtectionCertificatePath）",
                    fullPath);
            }

            try
            {
                var certificate = new X509Certificate2(
                    fullPath,
                    appConfig.AppSettings.DataProtectionCertificatePassword,
                    X509KeyStorageFlags.EphemeralKeySet);

                dataProtection.ProtectKeysWithCertificate(certificate);
                Serilog.Log.Information("Data Protection 密钥环已使用证书静态加密");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "加载 Data Protection 证书失败，请检查 PFX 路径与口令（AppSettings:DataProtectionCertificatePath/Password）",
                    ex);
            }
        }
        else if (builder.Environment.IsProduction())
        {
            Serilog.Log.Warning(
                "Data Protection 密钥环未配置静态加密证书，keys/ 目录中的密钥以明文落盘；" +
                "生产环境请设置 AppSettings:DataProtectionCertificatePath 与证书口令，并限制 keys/ 目录访问权限");
        }
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
        // 站点名称的跨组件同步状态（系统设置保存后，左侧品牌标题即时刷新）
        services.AddScoped<UserManagementSystem.Components.Shared.SiteInfoState>();
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
                // 瞬时故障重试：保留 3 次重试，但把最大退避从 10s 降到 2s。
                // 数据库整体不可用时，鉴权链路的降级路径可在数秒内触发，
                // 而不是把监控/诊断请求拖到 30s 以上（DEF-024）。
                mySqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(2),
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
                    // 数据库校验的短超时：数据库不可用时快速降级，避免每个请求被连接超时拖住数十秒。
                    using var dbCheckCts = CancellationTokenSource.CreateLinkedTokenSource(context.HttpContext.RequestAborted);
                    dbCheckCts.CancelAfter(TimeSpan.FromSeconds(3));
                    var dbCt = dbCheckCts.Token;

                    try
                    {
                        var principal = context.Principal;
                        if (principal is null)
                        {
                            context.Fail("Invalid token");
                            return;
                        }

                        var jti = principal.GetJti();
                        if (jti != null)
                        {
                            var blacklistService = context.HttpContext.RequestServices.GetRequiredService<ITokenBlacklistService>();
                            if (await blacklistService.IsBlacklistedAsync(jti, dbCt))
                            {
                                context.Fail("Token has been revoked");
                                return;
                            }
                        }

                        // 会话校验：JWT 中的 sid 指向 user_sessions。
                        // 撤销会话（踢下线、被新设备顶替、封禁）后，最迟 SessionValidationCacheSeconds
                        // 内所有实例都会拒绝该 token。旧版本令牌（无 sid）放行，兼容 ≤ExpiryMinutes 的过渡窗口。
                        var sessionId = principal.GetSessionId();
                        if (!string.IsNullOrEmpty(sessionId))
                        {
                            var sessionService = context.HttpContext.RequestServices.GetRequiredService<ISessionService>();
                            if (!await sessionService.IsSessionActiveAsync(sessionId, dbCt))
                            {
                                context.Fail("Session has been revoked");
                                return;
                            }

                            // 活跃时间节流上报（同会话 1 分钟内只写一次），供「已登录客户端」排序与在线统计
                            await sessionService.TouchAsync(
                                sessionId,
                                context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                                dbCt);
                        }

                        var userId = principal.GetUserId();
                        if (userId.HasValue)
                        {
                            // 用户状态缓存 30s，避免每个请求都查询 users 表；
                            // 键内嵌 user_status 版本号：状态变更（封禁/解封/删除）时版本号递增，
                            // 所有实例的旧键立即失效，不再有 30 秒的滞后窗口。
                            var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                            var versionService = context.HttpContext.RequestServices.GetRequiredService<ICacheVersionService>();
                            var statusVersion = await versionService.GetVersionAsync(CacheScopes.UserStatus, dbCt);
                            var cacheKey = UserService.UserStatusCacheKey(statusVersion, userId.Value);
                            if (!cache.TryGetValue(cacheKey, out UserStatus status))
                            {
                                var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
                                var user = await userService.GetByIdAsync(userId.Value, dbCt);
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
                    catch (OperationCanceledException) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
                    {
                        // 认证状态无法验证时必须 fail-closed：数据库校验超时说明黑名单/会话/用户状态
                        // 都未确认，不能放行，否则已撤销的令牌在数据库故障期间仍可访问受保护接口。
                        context.HttpContext.Items[AuthStateUnavailableItemKey] = true;
                        if (ShouldLogAuthStateFailure())
                        {
                            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                            logger.LogWarning("鉴权阶段的数据库校验超时（3s），拒绝认证（后续同类告警 30 秒内节流）");
                        }
                        context.Fail("Authentication state unavailable");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // 数据库整体不可用（连接中断、重试耗尽）时同样 fail-closed。
                        // 健康检查 /health 仍保持匿名可用，用于存活探测；受保护 API 不做降级放行。
                        context.HttpContext.Items[AuthStateUnavailableItemKey] = true;
                        if (ShouldLogAuthStateFailure())
                        {
                            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                            logger.LogWarning(ex, "鉴权阶段的数据库校验失败，拒绝认证（后续同类告警 30 秒内节流）");
                        }
                        context.Fail("Authentication state unavailable");
                    }
                },
                OnChallenge = async context =>
                {
                    // 数据库故障导致的“无法确认认证状态”返回 503，而不是 401：
                    // 401 会被管理前端当作会话失效并清空登录态，数据库恢复后还要重新登录。
                    if (!context.HttpContext.Items.ContainsKey(AuthStateUnavailableItemKey))
                    {
                        return; // 其他认证失败保持默认 401 行为
                    }

                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    context.Response.Headers.RetryAfter = "5";
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(
                        new ApiResponse(ResultCodes.InternalServerError, "认证状态暂不可用，请稍后重试")));
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
