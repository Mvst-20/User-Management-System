using System.Net;
using System.Net.Sockets;
using IP2Region.Net.Abstractions;
using IP2Region.Net.XDB;
using UserManagementSystem.Configuration;

namespace UserManagementSystem.Services.Security;

/// <summary>
/// IP 归属地解析。默认使用离线库 <c>ip2region.xdb</c>（无外网依赖、无隐私外泄），
/// 数据文件缺失或解析失败时降级为「未知」，绝不抛异常影响登录流程。
/// </summary>
public interface IIpLocationResolver
{
    /// <summary>返回「省 市」形式的中文归属地；无法解析时返回「未知」。</summary>
    string Resolve(string? ip);

    /// <summary>是否内网/回环地址（这类地址不参与异地判定）。</summary>
    bool IsPrivateOrLoopback(string? ip);

    /// <summary>IP 网段键（IPv4 取前三段）。归属地不可用时的兜底判定依据。</summary>
    string? NetworkKey(string? ip);
}

/// <inheritdoc cref="IIpLocationResolver"/>
public sealed class Ip2RegionLocationResolver : IIpLocationResolver, IDisposable
{
    public const string Unknown = "未知";

    private readonly ILogger<Ip2RegionLocationResolver> _logger;
    private readonly string _dbPath;
    private readonly object _initLock = new();

    private ISearcher? _searcher;
    private bool _initialized;

    public Ip2RegionLocationResolver(
        AppConfiguration appConfig,
        IWebHostEnvironment env,
        ILogger<Ip2RegionLocationResolver> logger)
    {
        _logger = logger;

        var configured = appConfig.Security.Ip2RegionDbPath;
        _dbPath = string.IsNullOrWhiteSpace(configured)
            ? string.Empty
            : Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(env.ContentRootPath, configured);
    }

    public string Resolve(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || IsPrivateOrLoopback(ip))
        {
            return Unknown;
        }

        var searcher = EnsureSearcher();
        if (searcher == null)
        {
            return Unknown;
        }

        try
        {
            var raw = searcher.Search(ip);
            return Format(raw);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IP 归属地解析失败：{Ip}", ip);
            return Unknown;
        }
    }

    public bool IsPrivateOrLoopback(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip, out var address))
        {
            return true;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        }

        var b = address.GetAddressBytes();
        return b[0] switch
        {
            10 => true,
            127 => true,
            0 => true,
            172 => b[1] >= 16 && b[1] <= 31,
            192 => b[1] == 168,
            169 => b[1] == 254,
            _ => false
        };
    }

    public string? NetworkKey(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip, out var address))
        {
            return null;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var groups = address.ToString().Split(':');
            return groups.Length >= 4 ? string.Join(':', groups.Take(4)) : address.ToString();
        }

        var bytes = address.GetAddressBytes();
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}";
    }

    /// <summary>把 "中国|0|广东省|深圳市|电信" 归一化为 "广东省 深圳市"。</summary>
    private static string Format(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Unknown;
        }

        var parts = raw.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 4)
        {
            return raw;
        }

        var country = parts[0];
        var province = parts[2];
        var city = parts[3];

        var segments = new List<string>(3);
        if (!IsPlaceholder(province))
        {
            segments.Add(province);
        }

        if (!IsPlaceholder(city) && city != province)
        {
            segments.Add(city);
        }

        if (segments.Count == 0)
        {
            return IsPlaceholder(country) ? Unknown : country;
        }

        return string.Join(' ', segments);
    }

    private static bool IsPlaceholder(string? value)
        => string.IsNullOrWhiteSpace(value) || value == "0";

    private ISearcher? EnsureSearcher()
    {
        if (_initialized)
        {
            return _searcher;
        }

        lock (_initLock)
        {
            if (_initialized)
            {
                return _searcher;
            }

            _initialized = true;

            if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath))
            {
                _logger.LogWarning(
                    "未找到离线 IP 库（{Path}），归属地将显示为「未知」，异地判定降级为网段比对。" +
                    "请下载 ip2region.xdb 放入该路径后重启。",
                    string.IsNullOrEmpty(_dbPath) ? "(未配置)" : _dbPath);
                return null;
            }

            try
            {
                // VectorIndex：只缓存向量索引（约数 MB），线程安全且比 File 模式少一次 IO
                _searcher = new Searcher(CachePolicy.VectorIndex, _dbPath);
                _logger.LogInformation("离线 IP 库已加载：{Path}", _dbPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "离线 IP 库加载失败：{Path}，归属地降级为「未知」", _dbPath);
                _searcher = null;
            }

            return _searcher;
        }
    }

    public void Dispose() => (_searcher as IDisposable)?.Dispose();
}
