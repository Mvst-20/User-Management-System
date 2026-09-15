namespace UserManagementSystem.Services.Api;

/// <summary>
/// 解析"管理前端 → 本进程 REST API"的回环调用基地址。
/// <para>
/// 解析优先级：
/// <list type="number">
///   <item>显式配置 <c>AppSettings:SelfBaseUrl</c>（自定义回环地址）</item>
///   <item><b>当前 HTTP 请求的 scheme + host</b>：该 host 必然已通过 HostFiltering 校验，
///         因此无需任何额外配置即可在"严格 AllowedHosts + 反向代理"的生产环境下工作</item>
///   <item>监听地址（<c>ASPNETCORE_URLS</c> / <c>urls</c>）推导，通配地址替换为 localhost</item>
/// </list>
/// </para>
/// </summary>
public sealed class SelfBaseUrlProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
    private string? _cached;

    public SelfBaseUrlProvider(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
    }

    /// <summary>获取基地址（不含结尾斜杠）。同一 circuit 内结果缓存。</summary>
    public string GetBaseUrl()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var configured = _configuration["AppSettings:SelfBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return _cached = Normalize(configured);
        }

        // 使用浏览器实际访问的地址：其 Host 已通过 HostFiltering，最稳妥
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is not null && request.Host.HasValue)
        {
            return _cached = $"{request.Scheme}://{request.Host.Value}";
        }

        return _cached = DeriveFromListenUrls(_configuration);
    }

    /// <summary>将相对路径解析为绝对地址；传入绝对地址时原样返回。</summary>
    public Uri Resolve(string pathOrUrl) => Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute)
        ? absolute
        : new Uri($"{GetBaseUrl()}/{pathOrUrl.TrimStart('/')}");

    /// <summary>从监听地址推导基地址（供启动日志与降级使用）。</summary>
    public static string DeriveFromListenUrls(IConfiguration configuration)
    {
        var urls = configuration["ASPNETCORE_URLS"] ?? configuration["urls"];
        if (!string.IsNullOrWhiteSpace(urls))
        {
            var candidate = urls
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase));
            if (candidate is not null)
            {
                return Normalize(candidate);
            }
        }

        return "http://localhost:5000";
    }

    /// <summary>规范化：通配监听地址替换为 localhost，去除结尾斜杠。</summary>
    public static string Normalize(string url) => url
        .Replace("0.0.0.0", "localhost", StringComparison.Ordinal)
        .Replace("[::]", "localhost", StringComparison.Ordinal)
        .Replace("*", "localhost", StringComparison.Ordinal)
        .TrimEnd('/');
}
