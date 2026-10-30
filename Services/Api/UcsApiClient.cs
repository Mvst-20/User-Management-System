using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using UserManagementSystem.Auth;
using UserManagementSystem.DTOs;

namespace UserManagementSystem.Services.Api;

/// <summary>
/// 管理前端访问 UCS REST API 的类型化客户端。
/// <para>
/// 前端运行于服务端（Interactive Server），通过回环 HTTP 调用本进程 API：
/// 既复用既有的 <c>RequirePermission</c> 端点过滤器（鉴权只有一处实现），
/// 又避免超长生命周期的 circuit 持有 scoped DbContext。
/// </para>
/// <para>
/// 令牌在过期前 60 秒自动刷新；收到 401 时尝试刷新并重放一次请求，
/// 仍失败则清理会话并触发 <see cref="SessionExpired"/>。
/// </para>
/// </summary>
public sealed class UcsApiClient
{
    private const int RefreshAheadSeconds = 60;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly TokenStore _store;
    private readonly SessionPersistence _persistence;
    private readonly SelfBaseUrlProvider _selfBase;
    private readonly ILogger<UcsApiClient> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public UcsApiClient(
        HttpClient http,
        TokenStore store,
        SessionPersistence persistence,
        SelfBaseUrlProvider selfBase,
        ILogger<UcsApiClient> logger)
    {
        _http = http;
        _store = store;
        _persistence = persistence;
        _selfBase = selfBase;
        _logger = logger;
    }

    /// <summary>会话失效（401 且无法刷新）时触发，供 UI 跳转登录页。</summary>
    public event Action? SessionExpired;

    // ==================== 会话 ====================

    /// <summary>登录并建立本地会话。</summary>
    public async Task<ApiResult<LoginDataResponse>> LoginAsync(string login, string password, CancellationToken ct = default)
    {
        var result = await PostAsync<LoginDataResponse>("/api/users/login", new LoginRequest(login, password), ct);
        if (result.Success && result.Data is { } data)
        {
            await EstablishSessionAsync(data);
        }

        return result;
    }

    /// <summary>登出：先通知服务端撤销令牌，再清理本地会话。</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (_store.IsAuthenticated)
        {
            try
            {
                await PostAsync<object>("/api/users/logout", null, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "登出接口调用失败，继续清理本地会话");
            }
        }

        await ClearSessionAsync();
    }

    /// <summary>
    /// 主动刷新访问令牌。已有其他请求先行刷新时直接复用其结果。
    /// </summary>
    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        var current = _store.Session;
        if (current?.RefreshToken is null)
        {
            return false;
        }

        var tokenAtEntry = current.AccessToken;

        await _refreshGate.WaitAsync(ct);
        try
        {
            // 并发请求可能已完成刷新，避免刷新风暴
            if (_store.Session?.AccessToken != tokenAtEntry)
            {
                return _store.IsAuthenticated;
            }

            using var request = CreateMessage(HttpMethod.Post, "/api/users/refresh",
                new RefreshTokenRequest(current.RefreshToken));
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            var envelope = JsonSerializer.Deserialize<Envelope<LoginDataResponse>>(body, JsonOptions);
            if (envelope?.Data is not { } data)
            {
                return false;
            }

            await EstablishSessionAsync(data);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "刷新访问令牌失败");
            return false;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>清理本地会话（内存 + 浏览器持久化）。</summary>
    public async Task ClearSessionAsync()
    {
        _store.Clear();
        await _persistence.ClearAsync();
    }

    /// <summary>获取当前登录用户的有效权限码集合（已展开特殊权限覆盖效果）。</summary>
    public Task<ApiResult<MyPermissionsResponse>> GetEffectivePermissionsAsync(CancellationToken ct = default)
        => GetAsync<MyPermissionsResponse>("/api/users/me/permissions", ct);

    /// <summary>轻量轮询：仅取全局权限版本号，用于判断本地权限快照是否过期。</summary>
    public Task<ApiResult<PermissionVersionResponse>> GetPermissionVersionAsync(CancellationToken ct = default)
        => GetAsync<PermissionVersionResponse>("/api/users/me/permissions/version", ct);

    // ==================== HTTP 动词 ====================

    public Task<ApiResult<T>> GetAsync<T>(string url, CancellationToken ct = default)
        => SendAsync<T>(() => new HttpRequestMessage(HttpMethod.Get, _selfBase.Resolve(url)), ct);

    public Task<ApiResult<T>> PostAsync<T>(string url, object? body = null, CancellationToken ct = default)
        => SendAsync<T>(() => CreateMessage(HttpMethod.Post, url, body), ct);

    public Task<ApiResult<T>> PutAsync<T>(string url, object? body = null, CancellationToken ct = default)
        => SendAsync<T>(() => CreateMessage(HttpMethod.Put, url, body), ct);

    public Task<ApiResult<T>> PatchAsync<T>(string url, object? body = null, CancellationToken ct = default)
        => SendAsync<T>(() => CreateMessage(HttpMethod.Patch, url, body), ct);

    public Task<ApiResult<T>> DeleteAsync<T>(string url, CancellationToken ct = default)
        => SendAsync<T>(() => new HttpRequestMessage(HttpMethod.Delete, _selfBase.Resolve(url)), ct);

    /// <summary>
    /// 以 multipart/form-data 上传单个文件（当前用于头像）。
    /// 与其它动词共享令牌注入、过期前自动刷新与 401 重放逻辑。
    /// </summary>
    public Task<ApiResult<T>> PostFileAsync<T>(
        string url,
        Stream file,
        string fileName,
        string fieldName = "file",
        string? contentType = null,
        CancellationToken ct = default)
        => SendAsync<T>(() =>
        {
            // 401 重放会再次调用本工厂，需把流复位到起点重复读取
            if (file.CanSeek)
            {
                file.Position = 0;
            }

            var form = new MultipartFormDataContent();
            var part = new StreamContent(file);
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            }

            form.Add(part, fieldName, fileName);

            return new HttpRequestMessage(HttpMethod.Post, _selfBase.Resolve(url))
            {
                Content = form
            };
        }, ct);

    public Task<PagedResult<T>> GetPagedAsync<T>(string url, CancellationToken ct = default)
        => SendPagedAsync<T>(() => new HttpRequestMessage(HttpMethod.Get, _selfBase.Resolve(url)), ct);

    // ==================== 内部实现 ====================

    private async Task<ApiResult<T>> SendAsync<T>(Func<HttpRequestMessage> factory, CancellationToken ct)
    {
        var raw = await SendRawAsync(factory, ct);
        return ParseEnvelope<T>(raw);
    }

    private async Task<PagedResult<T>> SendPagedAsync<T>(Func<HttpRequestMessage> factory, CancellationToken ct)
    {
        var raw = await SendRawAsync(factory, ct);
        return ParsePaged<T>(raw);
    }

    private async Task<RawResponse> SendRawAsync(Func<HttpRequestMessage> factory, CancellationToken ct)
    {
        await RefreshIfNeededAsync(ct);

        HttpResponseMessage response;
        try
        {
            response = await SendOnceAsync(factory, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return TransportFailure(ex);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();

            if (await RefreshAsync(ct))
            {
                try
                {
                    response = await SendOnceAsync(factory, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return TransportFailure(ex);
                }
            }
            else
            {
                await ClearSessionAsync();
                SessionExpired?.Invoke();
                return new RawResponse(HttpStatusCode.Unauthorized, string.Empty);
            }
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var status = response.StatusCode;
        response.Dispose();

        return new RawResponse(status, body);
    }

    /// <summary>
    /// 回环调用在传输层失败（连接被拒 / 超时 / DNS / Host 校验被拒）时的统一处理。
    /// 绝不把异常抛给 UI 事件处理器——否则按钮会停留在加载态，表现为"点击无响应"。
    /// </summary>
    private RawResponse TransportFailure(Exception ex)
    {
        var baseUrl = _selfBase.GetBaseUrl();
        _logger.LogError(ex, "调用本进程 API 失败（基地址 {BaseUrl}）", baseUrl);

        return new RawResponse(HttpStatusCode.BadGateway, string.Empty,
            $"无法连接接口服务（{baseUrl}），请检查服务地址与反向代理配置");
    }

    private async Task<HttpResponseMessage> SendOnceAsync(Func<HttpRequestMessage> factory, CancellationToken ct)
    {
        var request = factory();
        if (_store.AccessToken is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _http.SendAsync(request, ct);
    }

    private async Task RefreshIfNeededAsync(CancellationToken ct)
    {
        if (_store.NeedsRefresh(TimeSpan.FromSeconds(RefreshAheadSeconds)))
        {
            await RefreshAsync(ct);
        }
    }

    private HttpRequestMessage CreateMessage(HttpMethod method, string url, object? body)
    {
        // 按当前请求来源解析绝对地址：避免生产环境下回环地址的 Host 未登记于 AllowedHosts 而被 400 拒绝
        var message = new HttpRequestMessage(method, _selfBase.Resolve(url));
        if (body is not null)
        {
            message.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return message;
    }

    private async Task EstablishSessionAsync(LoginDataResponse data)
    {
        var session = new LoginSession(
            data.Token,
            data.RefreshToken,
            DateTime.UtcNow.AddMinutes(data.ExpiresIn),
            data.User.Id,
            data.User.Username,
            data.User.Email,
            data.User.AvatarUrl);

        _store.SetSession(session);
        await _persistence.SaveAsync(session);
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static ApiResult<T> ParseEnvelope<T>(RawResponse raw)
    {
        if (raw.TransportError is { } transportError)
        {
            return ApiResult<T>.Fail(ResultCodes.InternalServerError, transportError);
        }

        if (raw.Status == HttpStatusCode.Unauthorized)
        {
            return ApiResult<T>.Fail(ResultCodes.Unauthorized, "登录状态已失效，请重新登录");
        }

        if (string.IsNullOrWhiteSpace(raw.Body))
        {
            return IsSuccess(raw.Status)
                ? ApiResult<T>.Ok(default)
                : ApiResult<T>.Fail((int)raw.Status, "请求失败，请稍后重试");
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope<T>>(raw.Body, JsonOptions);
            if (envelope is null)
            {
                return ApiResult<T>.Fail((int)raw.Status, "响应格式不正确");
            }

            return IsSuccess(raw.Status)
                ? ApiResult<T>.Ok(envelope.Data, envelope.Message ?? "操作成功", envelope.Code)
                : ApiResult<T>.Fail(envelope.Code != 0 ? envelope.Code : (int)raw.Status, envelope.Message ?? "请求失败");
        }
        catch (JsonException ex)
        {
            return ApiResult<T>.Fail((int)raw.Status, $"响应解析失败：{ex.Message}");
        }
    }

    private static PagedResult<T> ParsePaged<T>(RawResponse raw)
    {
        if (raw.TransportError is { } transportError)
        {
            return PagedResult<T>.Fail(ResultCodes.InternalServerError, transportError);
        }

        if (raw.Status == HttpStatusCode.Unauthorized)
        {
            return PagedResult<T>.Fail(ResultCodes.Unauthorized, "登录状态已失效，请重新登录");
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<PagedEnvelope<T>>(raw.Body, JsonOptions);
            if (envelope is null)
            {
                return PagedResult<T>.Fail((int)raw.Status, "响应格式不正确");
            }

            return IsSuccess(raw.Status)
                ? PagedResult<T>.Ok(envelope.Items ?? new List<T>(), envelope.Page, envelope.PageSize,
                    envelope.TotalCount, envelope.Message ?? "获取成功", envelope.Code)
                : PagedResult<T>.Fail(envelope.Code != 0 ? envelope.Code : (int)raw.Status, envelope.Message ?? "请求失败");
        }
        catch (JsonException ex)
        {
            return PagedResult<T>.Fail((int)raw.Status, $"响应解析失败：{ex.Message}");
        }
    }

    private sealed record Envelope<T>(int Code, string? Message, T? Data);

    private sealed record PagedEnvelope<T>(int Code, string? Message, List<T>? Items, int Page, int PageSize, int TotalCount);

    private sealed record RawResponse(HttpStatusCode Status, string Body, string? TransportError = null);
}
