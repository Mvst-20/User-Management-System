using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using UserManagementSystem.Services;

namespace UserManagementSystem.Auth;

/// <summary>
/// 子系统 API Key 认证处理器。
/// <para>
/// 凭证通过 <c>X-Api-Key</c> + <c>X-Api-Secret</c> 请求头传递；
/// 校验通过后生成"子系统主体"，用于与用户 JWT 身份区分。
/// </para>
/// <para>
/// 注意：主体中**不写入** <see cref="ClaimTypes.NameIdentifier"/>，
/// 因此既有 <c>RequirePermission</c> 过滤器在需要用户权限的端点上会自然拒绝子系统调用，
/// 使子系统凭证的有效访问范围收敛为"仅需认证、无需用户权限"的端点（即权限检查接口）。
/// </para>
/// </summary>
public sealed class SubsystemKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string ApiSecretHeader = "X-Api-Secret";

    /// <summary>子系统 ID 声明（用于在端点中识别子系统调用方）。</summary>
    public const string SubsystemIdClaim = "ucs:subsystem_id";

    /// <summary>子系统标识声明。</summary>
    public const string SubsystemCodeClaim = "ucs:subsystem_code";

    private readonly ISubsystemService _subsystemService;

    public SubsystemKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISubsystemService subsystemService)
        : base(options, logger, encoder)
    {
        _subsystemService = subsystemService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyHeader, out var apiKeyValues) ||
            !Request.Headers.TryGetValue(ApiSecretHeader, out var apiSecretValues))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKey = apiKeyValues.ToString();
        var apiSecret = apiSecretValues.ToString();

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            return AuthenticateResult.Fail("缺少子系统凭证");
        }

        var subsystem = await _subsystemService.ValidateCredentialAsync(apiKey, apiSecret, Context.RequestAborted);
        if (subsystem is null)
        {
            return AuthenticateResult.Fail("子系统凭证无效、已停用或已过期");
        }

        // 记录最近使用时间（服务内部节流，失败不影响认证结果）
        try
        {
            await _subsystemService.TouchAsync(subsystem.Id, Context.RequestAborted);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "更新子系统最近使用时间失败");
        }

        var claims = new List<Claim>
        {
            new(SubsystemIdClaim, subsystem.Id.ToString()),
            new(SubsystemCodeClaim, subsystem.Code),
            new(ClaimTypes.Name, subsystem.Name)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
