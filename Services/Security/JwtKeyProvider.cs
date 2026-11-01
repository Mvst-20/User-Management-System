using System.Security.Cryptography;
using UserManagementSystem.Configuration;

namespace UserManagementSystem.Services.Security;

/// <summary>
/// JWT 签名密钥的解析与持久化。
/// <para>
/// 安全要求：仓库与 <c>appsettings.json</c> 中不得包含可用的固定密钥。
/// 优先级：
/// <list type="number">
///   <item>配置 <c>Jwt:Key</c>（环境变量 <c>Jwt__Key</c>）——生产部署推荐；</item>
///   <item>本地密钥文件 <c>keys/jwt-signing.key</c>（该目录已在 .gitignore 中）；</item>
///   <item>自动生成随机密钥并写入该文件（<c>Jwt:RequireExternalKey=false</c> 时的兜底）。</item>
/// </list>
/// </para>
/// <para>
/// 轮换密钥会使所有已签发的 Access Token 立即失效，需在维护窗口执行。
/// </para>
/// </summary>
public static class JwtKeyProvider
{
    /// <summary>本地密钥文件名（位于 Data Protection 密钥目录下）。</summary>
    public const string KeyFileName = "jwt-signing.key";

    /// <summary>解析可用密钥；返回密钥与可选的告警文案。</summary>
    /// <exception cref="InvalidOperationException">
    /// 严格模式（<see cref="JwtConfig.RequireExternalKey"/>=true）下未配置可用密钥时抛出。
    /// </exception>
    public static string Resolve(JwtConfig config, string keysDirectory, out string? warning)
    {
        warning = null;

        if (IsUsable(config.Key))
        {
            return config.Key.Trim();
        }

        if (config.RequireExternalKey)
        {
            throw new InvalidOperationException(
                "未配置 JWT 签名密钥。请通过环境变量 Jwt__Key（或配置项 Jwt:Key）提供至少 64 个字符的随机字符串；" +
                "当前要求必须由外部注入（Jwt:RequireExternalKey=true），不会自动生成。");
        }

        Directory.CreateDirectory(keysDirectory);
        var path = Path.Combine(keysDirectory, KeyFileName);

        try
        {
            if (File.Exists(path))
            {
                var stored = File.ReadAllText(path).Trim();
                if (IsUsable(stored))
                {
                    return stored;
                }
            }
        }
        catch (Exception ex)
        {
            warning = $"读取 JWT 密钥文件失败（{path}）：{ex.Message}；将重新生成。";
        }

        var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        File.WriteAllText(path, generated);

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // 权限设置失败不影响可用性；生产环境应通过环境变量注入密钥
        }

        warning ??= $"未配置 JWT 签名密钥，已生成并保存到 {path}。" +
                    "多实例部署请改用环境变量 Jwt__Key 共享同一密钥；轮换后旧令牌将全部失效。";
        return generated;
    }

    /// <summary>密钥是否可用：≥64 字符，且不是模板占位符。</summary>
    private static bool IsUsable(string? key)
        => !string.IsNullOrWhiteSpace(key)
           && key.Trim().Length >= 64
           && !key.Trim().StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
}
