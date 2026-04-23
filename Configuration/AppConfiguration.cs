namespace UserManagementSystem.Configuration;

public class AppConfiguration
{
    public JwtConfig Jwt { get; set; } = new();
    public SmtpConfig Smtp { get; set; } = new();
    public AppSettings AppSettings { get; set; } = new();
}

public class JwtConfig
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 120;
}

public class SmtpConfig
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
}

public class AppSettings
{
    public string ApiBaseUrl { get; set; } = string.Empty;
    public int TokenExpiryMinutes { get; set; } = 20;
    public int AdminPasswordLength { get; set; } = 12;
    public string AllowedOrigins { get; set; } = string.Empty; // 逗号分隔的允许跨域来源，为空则允许所有
}
