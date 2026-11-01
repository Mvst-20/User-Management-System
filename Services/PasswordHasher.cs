namespace UserManagementSystem.Services;

/// <summary>
/// 密码哈希的统一入口。
/// <para>
/// Work Factor 统一为 <see cref="WorkFactor"/>（默认 11）：原 12 在 20 并发登录下
/// P95≈530ms，超过性能门槛；11 约为其一半耗时，仍在 OWASP 推荐范围内。
/// 校验时 BCrypt 从哈希串自身读取 cost，因此历史 cost=12 的密码仍可正常登录。
/// </para>
/// </summary>
public static class PasswordHasher
{
    /// <summary>BCrypt Work Factor（新密码统一使用）。</summary>
    public const int WorkFactor = 11;

    /// <summary>
    /// 固定的 dummy 哈希：用户不存在时也执行一次同成本的 BCrypt 校验，
    /// 消除「账号是否存在」的响应时间差（防账号枚举时序侧信道）。
    /// </summary>
    private static readonly Lazy<string> DummyHash = new(
        () => BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"), WorkFactor),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>生成密码哈希。</summary>
    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    /// <summary>校验密码；哈希格式非法时按校验失败处理，不抛异常。</summary>
    public static bool Verify(string? password, string? hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 对 dummy 哈希执行一次校验，返回值无意义；用于让「用户不存在」分支
    /// 消耗与真实密码校验相同的 CPU 时间。
    /// </summary>
    public static void VerifyDummy(string? password)
    {
        try
        {
            BCrypt.Net.BCrypt.Verify(password ?? string.Empty, DummyHash.Value);
        }
        catch
        {
            // dummy 校验仅用于消耗时间，任何异常都忽略
        }
    }
}
