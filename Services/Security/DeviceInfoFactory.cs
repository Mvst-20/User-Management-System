using System.Security.Cryptography;
using System.Text;
using UserManagementSystem.DTOs;

namespace UserManagementSystem.Services.Security;

/// <summary>
/// User-Agent 解析：提取「浏览器 · 操作系统」用于展示，并据此生成设备归类键。
/// <para>
/// 归类键是「同类设备只保留最近一次登录」的判定依据，因此只取浏览器 + 系统，
/// 不包含版本号——同一台机器升级浏览器后仍被视为同一类设备。
/// </para>
/// </summary>
public static class UserAgentParser
{
    public static (string Name, string Type, string Key) Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return ("未知设备", "Unknown", HashKey("unknown", "unknown"));
        }

        var ua = userAgent;
        var browser = DetectBrowser(ua);
        var os = DetectOs(ua);
        var type = DetectType(ua);

        return ($"{browser} · {os}", type, HashKey(browser, os));
    }

    private static string DetectBrowser(string ua)
    {
        if (ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("Edge/", StringComparison.OrdinalIgnoreCase))
        {
            return "Edge";
        }

        if (ua.Contains("OPR/", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("Opera", StringComparison.OrdinalIgnoreCase))
        {
            return "Opera";
        }

        if (ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
        {
            return "Firefox";
        }

        if (ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("CriOS", StringComparison.OrdinalIgnoreCase))
        {
            return "Chrome";
        }

        if (ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase))
        {
            return "Safari";
        }

        if (ua.Contains("MSIE ", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("Trident/", StringComparison.OrdinalIgnoreCase))
        {
            return "IE";
        }

        if (ua.StartsWith("curl/", StringComparison.OrdinalIgnoreCase))
        {
            return "curl";
        }

        if (ua.Contains("python-requests", StringComparison.OrdinalIgnoreCase))
        {
            return "python-requests";
        }

        return "未知浏览器";
    }

    private static string DetectOs(string ua)
    {
        if (ua.Contains("Windows NT 10.0", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows 10/11";
        }

        if (ua.Contains("Windows NT 6.3", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows 8.1";
        }

        if (ua.Contains("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows";
        }

        if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase))
        {
            return "Android";
        }

        if (ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("iPod", StringComparison.OrdinalIgnoreCase))
        {
            return "iOS";
        }

        if (ua.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("Macintosh", StringComparison.OrdinalIgnoreCase))
        {
            return "macOS";
        }

        if (ua.Contains("Linux", StringComparison.OrdinalIgnoreCase))
        {
            return "Linux";
        }

        return "未知系统";
    }

    private static string DetectType(string ua)
    {
        if (ua.Contains("bot", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("spider", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("crawler", StringComparison.OrdinalIgnoreCase))
        {
            return "Bot";
        }

        if (ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("Tablet", StringComparison.OrdinalIgnoreCase))
        {
            return "Tablet";
        }

        // Android 平板通常不带 "Mobile"，手机 UA 才会带
        if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase) &&
            !ua.Contains("Mobile", StringComparison.OrdinalIgnoreCase))
        {
            return "Tablet";
        }

        if (ua.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("Android", StringComparison.OrdinalIgnoreCase))
        {
            return "Mobile";
        }

        return "Desktop";
    }

    private static string HashKey(string browser, string os)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{browser.ToLowerInvariant()}|{os.ToLowerInvariant()}"));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }
}

/// <summary>从 HTTP 请求构造 <see cref="DeviceInfo"/>（登录、刷新、登出共用）。</summary>
public static class DeviceInfoFactory
{
    private const int UserAgentMaxLength = 512;
    private const int IpMaxLength = 45;

    public static DeviceInfo FromRequest(HttpContext http, IIpLocationResolver locations)
    {
        var userAgent = http.Request.Headers.UserAgent.ToString();
        var ip = Truncate(http.Connection.RemoteIpAddress?.ToString(), IpMaxLength);
        var (name, type, key) = UserAgentParser.Parse(userAgent);

        return new DeviceInfo(
            key,
            name,
            type,
            Truncate(userAgent, UserAgentMaxLength),
            ip,
            locations.Resolve(ip));
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
