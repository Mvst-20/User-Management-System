using System.Reflection;

namespace UserManagementSystem.Configuration;

/// <summary>
/// 应用级常量
/// </summary>
public static class AppInfo
{
    /// <summary>
    /// 应用版本号：统一取自程序集 <c>InformationalVersion</c>（项目文件中的 Version），
    /// 避免启动横幅、系统信息接口与发布文档各自维护导致版本不一致。
    /// </summary>
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // 去掉 SourceRevisionId 追加的 "+<commit>" 后缀
            var plus = informational.IndexOf('+');
            if (plus >= 0)
            {
                informational = informational[..plus];
            }

            informational = informational.Trim();
            return informational.StartsWith('v') ? informational : "v" + informational;
        }

        return "v1.5.0";
    }
}

public class AppConfiguration
{
    public JwtConfig Jwt { get; set; } = new();
    public AppSettings AppSettings { get; set; } = new();
    public PermissionsConfig Permissions { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
}

public class JwtConfig
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 120;

    /// <summary>
    /// 是否要求 JWT 密钥必须由外部注入（环境变量 <c>Jwt__Key</c> / 配置项 <c>Jwt:Key</c>）。
    /// 为 true 时缺失可用密钥将直接启动失败；为 false 时允许自动生成到 keys/ 目录（本地开发兜底）。
    /// </summary>
    public bool RequireExternalKey { get; set; }

    /// <summary>Refresh Token（会话）有效期天数。</summary>
    public int RefreshTokenDays { get; set; } = 30;
}

/// <summary>
/// 权限缓存相关参数。缓存键带全局版本前缀，写操作 bump 版本号即可让所有实例的
/// 旧键失效，因此 <see cref="CacheTtlMinutes"/> 仅作兜底，不再承担"生效延迟"的角色。
/// </summary>
public class PermissionsConfig
{
    /// <summary>权限缓存 TTL（分钟），仅兜底使用。</summary>
    public int CacheTtlMinutes { get; set; } = 2;

    /// <summary>版本号在内存中的缓存秒数：决定跨实例失效的传播延迟上限。0 表示不做内存缓存。</summary>
    public int VersionCacheSeconds { get; set; } = 1;

    /// <summary>管理控制台轮询权限版本号的间隔秒数；≤0 表示关闭前端自动刷新。</summary>
    public int ClientPollSeconds { get; set; } = 30;
}

/// <summary>
/// 会话与登录安全相关参数。
/// </summary>
public class SecurityConfig
{
    /// <summary>会话有效性校验结论的缓存秒数（只缓存"有效"，撤销后立即跨实例生效）。</summary>
    public int SessionValidationCacheSeconds { get; set; } = 5;

    /// <summary>登录历史保留天数，超期由清理任务删除。</summary>
    public int LoginHistoryRetentionDays { get; set; } = 180;

    /// <summary>判定「新设备」时回溯的天数。</summary>
    public int LocationHistoryDays { get; set; } = 90;

    /// <summary>新设备登录是否发送提醒邮件。</summary>
    public bool AlertOnNewDevice { get; set; } = true;

    /// <summary>异地登录是否发送提醒邮件。</summary>
    public bool AlertOnNewLocation { get; set; } = true;

    /// <summary>同一用户 + 同一设备 + 同一地点的提醒去重窗口（小时）。</summary>
    public int AlertDedupHours { get; set; } = 24;

    /// <summary>离线 IP 库文件路径（相对路径基于内容根目录）。留空或文件缺失时归属地降级为「未知」。</summary>
    public string Ip2RegionDbPath { get; set; } = "App_Data/ip2region.xdb";
}

public class AppSettings
{
    public string ApiBaseUrl { get; set; } = string.Empty;
    public int TokenExpiryMinutes { get; set; } = 20;
    public int AdminPasswordLength { get; set; } = 12;
    public string AllowedOrigins { get; set; } = string.Empty;

    /// <summary>
    /// 管理前端回环调用本进程 REST API 的基地址。<b>可选覆盖项，一般无需配置</b>：
    /// 留空时自动使用运行时真实监听地址（多地址优先 http，通配地址规范化为 localhost）。
    /// 仅在自动解析的结果无法从本机访问时才需显式填写；两者都取不到时应用启动即失败并提示本项。
    /// </summary>
    public string SelfBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Data Protection 密钥环持久化目录（SMTP 密码加密依赖该密钥环，丢失将无法解密）。
    /// 相对路径基于应用工作目录；留空时使用 "keys"。
    /// </summary>
    public string DataProtectionKeysPath { get; set; } = string.Empty;
}
