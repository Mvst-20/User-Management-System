using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace UserManagementSystem.Services.Api;

/// <summary>
/// 解析"管理前端 → 本进程 REST API"的回环调用基地址。
/// <para>
/// 唯一权威来源是<b>进程真实监听的地址</b>（<see cref="IServerAddressesFeature"/>，与 <c>app.Urls</c> 同源，
/// 由宿主在 <c>StartAsync</c> 时填好，与地址来自 launchSettings / <c>--urls</c> / <c>ASPNETCORE_URLS</c> /
/// <c>HTTP_PORTS</c> / IIS-ANCM 动态端口无关）。
/// </para>
/// <para>
/// 解析顺序：
/// <list type="number">
///   <item>可选覆盖 <c>AppSettings:SelfBaseUrl</c>（应急用，一般不配）</item>
///   <item>真实监听地址；多地址时优先 <c>http</c>（回环走明文可避免自签证书校验失败）</item>
/// </list>
/// 两者都取不到时<b>不返回假地址</b>：<see cref="TryGetBaseUrl"/> 返回 false 并给出指引文本，
/// <see cref="GetBaseUrl"/> 直接抛异常（由启动流程终止进程），而不是静默回落到一个没人监听的地址。
/// </para>
/// <para>
/// 地址在进程启动后被缓存（服务注册为单例），保证启动横幅、HostFiltering 白名单与回环调用三处结果一致。
/// </para>
/// </summary>
public sealed class SelfBaseUrlProvider
{
    /// <summary>可选覆盖项的配置键。留空时自动使用运行时监听地址。</summary>
    public const string ConfigKey = "AppSettings:SelfBaseUrl";

    private readonly IConfiguration _configuration;
    private readonly IServer _server;
    private readonly object _gate = new();
    private volatile string? _cached;

    public SelfBaseUrlProvider(IConfiguration configuration, IServer server)
    {
        _configuration = configuration;
        _server = server;
    }

    /// <summary>
    /// 解析基地址（不含结尾斜杠）。失败时返回 false 并给出可操作的指引文本，<b>不抛异常</b>
    /// —— 供"绝不能让异常冒泡到 UI 事件处理器"的兜底路径（<see cref="UcsApiClient"/>）使用。
    /// </summary>
    public bool TryGetBaseUrl(out string baseUrl, out string? error)
    {
        if (_cached is { } cached)
        {
            baseUrl = cached;
            error = null;
            return true;
        }

        lock (_gate)
        {
            if (_cached is { } raced)
            {
                baseUrl = raced;
                error = null;
                return true;
            }

            if (TryResolve(out var resolved, out error))
            {
                _cached = resolved;
                baseUrl = resolved;
                error = null;
                return true;
            }

            baseUrl = string.Empty;
            return false;
        }
    }

    /// <summary>同 <see cref="TryGetBaseUrl"/>，但失败时抛出异常（用于启动阶段快速失败）。</summary>
    public string GetBaseUrl()
        => TryGetBaseUrl(out var baseUrl, out var error)
            ? baseUrl
            : throw new InvalidOperationException(error);

    /// <summary>将相对路径解析为绝对地址；传入绝对地址时原样返回。</summary>
    public Uri Resolve(string pathOrUrl) => Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute)
        ? absolute
        : new Uri($"{GetBaseUrl()}/{pathOrUrl.TrimStart('/')}");

    /// <summary>
    /// 把回环调用地址的 Host 并入 <see cref="HostFilteringOptions.AllowedHosts"/>。
    /// <para>
    /// 生产环境的 <c>AllowedHosts</c> 通常只登记对外域名（如 <c>api.example.com</c>），而控制台会以本机地址
    /// 回环调用自身 API。该 Host 若未登记，会被 HostFiltering 直接以 400 Invalid Hostname 拒绝，
    /// 表现为界面所有数据请求失败（登录按钮无反应或"响应解析失败"）。
    /// </para>
    /// <para>
    /// <b>两个必须遵守的前提</b>（否则调用完全无效，且不会报错）：
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     传入的实例必须是 <c>IOptionsMonitor&lt;HostFilteringOptions&gt;.CurrentValue</c>，
    ///     而<b>不是</b> <c>IOptions&lt;HostFilteringOptions&gt;.Value</c>：
    ///     <c>HostFilteringMiddleware</c> 只持有 monitor 的那一个实例（<c>_options = optionsMonitor.CurrentValue</c>），
    ///     <c>IOptions&lt;T&gt;</c> 与 <c>IOptionsMonitor&lt;T&gt;</c> 各自缓存<b>不同的实例</b>，改错对象不会生效。
    ///   </item>
    ///   <item>
    ///     必须在服务器启动之后、<b>首个请求之前</b>调用：监听地址在 <c>IServer.StartAsync</c> 后才可见，
    ///     而中间件会在首次请求时把 <c>AllowedHosts</c> 解析成内部快照并长期复用，之后再改就来不及了。
    ///   </item>
    /// </list>
    /// </summary>
    public void EnsureHostAllowed(HostFilteringOptions options)
    {
        if (!TryGetBaseUrl(out var baseUrl, out _))
        {
            return;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

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
    }

    private bool TryResolve(out string baseUrl, out string? error)
    {
        var configured = _configuration[ConfigKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var normalized = Normalize(configured);
            if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri) && IsHttp(uri))
            {
                baseUrl = normalized;
                error = null;
                return true;
            }

            baseUrl = string.Empty;
            error = $"配置项 {ConfigKey} 不是合法的 http/https 绝对地址（当前值：{configured}）。";
            return false;
        }

        var fromServer = DeriveFromServerAddresses();
        if (fromServer is not null)
        {
            baseUrl = fromServer;
            error = null;
            return true;
        }

        baseUrl = string.Empty;
        error = "无法确定控制台回环调用本进程 API 的基地址：进程未报告任何 HTTP/HTTPS 监听地址。" +
                "请确认已绑定监听地址（launchSettings 的 applicationUrl、--urls、ASPNETCORE_URLS 或 HTTP_PORTS），" +
                $"或在 http/https 均不可从本机访问时显式设置配置项 {ConfigKey}（例如 https://api.example.com）。";
        return false;
    }

    /// <summary>从 <see cref="IServer"/> 的真实监听地址推导基地址；取不到时返回 null。</summary>
    private string? DeriveFromServerAddresses()
    {
        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses is null || addresses.Count == 0)
        {
            return null;
        }

        Uri? fallback = null;
        foreach (var address in addresses)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }

            var normalized = Normalize(address);
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || !IsHttp(uri))
            {
                continue;
            }

            // 优先 http：回环走明文可避免自签证书校验失败
            if (uri.Scheme == Uri.UriSchemeHttp)
            {
                return uri.GetLeftPart(UriPartial.Authority);
            }

            fallback ??= uri;
        }

        return fallback?.GetLeftPart(UriPartial.Authority);
    }

    private static bool IsHttp(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>
    /// 规范化：通配监听地址（<c>0.0.0.0</c>、<c>[::]</c>、<c>*</c>、<c>+</c>）替换为 <c>localhost</c>
    /// （本机必然可达），并去除结尾斜杠。仅处理 authority 段，避免误伤路径与查询中的同名字符。
    /// </summary>
    public static string Normalize(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmed = url.Trim().TrimEnd('/');

        var schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            return trimmed;
        }

        var authorityStart = schemeEnd + 3;
        var authorityEnd = trimmed.IndexOf('/', authorityStart);
        if (authorityEnd < 0)
        {
            authorityEnd = trimmed.Length;
        }

        if (authorityStart >= authorityEnd)
        {
            return trimmed;
        }

        var authority = trimmed[authorityStart..authorityEnd];
        var normalizedAuthority = authority
            .Replace("[::]", "localhost", StringComparison.Ordinal)
            .Replace("0.0.0.0", "localhost", StringComparison.Ordinal);

        // "*" / "+" 既可能是全部（"*"）也可能是带端口的写法（"*:5208"）
        if (normalizedAuthority.StartsWith('*') || normalizedAuthority.StartsWith('+'))
        {
            normalizedAuthority = "localhost" + normalizedAuthority[1..];
        }

        return normalizedAuthority == authority
            ? trimmed
            : trimmed[..authorityStart] + normalizedAuthority + trimmed[authorityEnd..];
    }
}
