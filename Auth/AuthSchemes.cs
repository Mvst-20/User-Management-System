namespace UserManagementSystem.Auth;

/// <summary>
/// 认证方案名称常量。
/// </summary>
public static class AuthSchemes
{
    /// <summary>JWT Bearer（用户登录态，既有方案）。</summary>
    public const string Jwt = "Bearer";

    /// <summary>子系统 API Key 凭证。</summary>
    public const string SubsystemKey = "SubsystemKey";

    /// <summary>
    /// 多认证分派方案（默认方案）：按请求头自动选择 JWT 或子系统凭证。
    /// 既有调用方（Authorization: Bearer）行为保持不变。
    /// </summary>
    public const string Multi = "MultiAuth";
}

/// <summary>
/// 授权策略名称常量。
/// </summary>
public static class AuthPolicies
{
    /// <summary>
    /// 仅允许子系统凭证（X-Api-Key + X-Api-Secret）访问的策略。
    /// 用于子系统自注册权限等端点——用户 JWT 无法通过该策略，
    /// 且子系统主体不含 NameIdentifier，也不会被 RequirePermission 端点放行，两类调用方互不可越界。
    /// </summary>
    public const string SubsystemKey = "RequireSubsystemKey";
}
