namespace UserManagementSystem.Configuration;

/// <summary>
/// 应用级常量
/// </summary>
public static class AppInfo
{
    public const string Version = "v1.0.0";
}

public class AppConfiguration
{
    public JwtConfig Jwt { get; set; } = new();
    public AppSettings AppSettings { get; set; } = new();
}

public class JwtConfig
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 120;
}

public class AppSettings
{
    public string ApiBaseUrl { get; set; } = string.Empty;
    public int TokenExpiryMinutes { get; set; } = 20;
    public int AdminPasswordLength { get; set; } = 12;
    public string AllowedOrigins { get; set; } = string.Empty;

    /// <summary>
    /// 管理前端回环调用本进程 REST API 的基地址。
    /// 留空时自动从 ASPNETCORE_URLS/urls 推导，最终回落到 http://localhost:5000。
    /// </summary>
    public string SelfBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Data Protection 密钥环持久化目录（SMTP 密码加密依赖该密钥环，丢失将无法解密）。
    /// 相对路径基于应用工作目录；留空时使用 "keys"。
    /// </summary>
    public string DataProtectionKeysPath { get; set; } = string.Empty;
}
