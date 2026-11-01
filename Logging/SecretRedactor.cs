using System.Text.RegularExpressions;

namespace UserManagementSystem.Logging;

/// <summary>
/// 日志/启动错误中的凭据脱敏（纵深防御）。
/// <para>
/// 主要防线仍在源头：数据库连接串在交给驱动前已拆出 user/password，
/// 启动账号口令只写控制台。本类用于兜底，避免任何遗漏路径把
/// <c>password=...</c>、<c>user:password@host</c>、<c>Password: ...</c>
/// 或 Bearer Token 写入文件/内存日志。
/// </para>
/// </summary>
public static class SecretRedactor
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

    private static readonly Regex[] Patterns =
    [
        // 连接串：password=xxx / pwd=xxx
        new(@"(?i)(password\s*=\s*)([^;\s]+)", RegexOptions.Compiled, MatchTimeout),
        new(@"(?i)(pwd\s*=\s*)([^;\s]+)", RegexOptions.Compiled, MatchTimeout),

        // 连接串：server=user:password@host（只替换 @ 前的口令部分）
        new(@"(?i)(server\s*=\s*[^;@\s]*:)[^@;\s]+@", RegexOptions.Compiled, MatchTimeout),

        // 初始化口令 / 邮件模板等文本：Password: xxx
        new(@"(?i)(password\s*:\s*)(\S+)", RegexOptions.Compiled, MatchTimeout),

        // 子系统 Secret / JWT Key
        new(@"(?i)(api[_-]?secret\s*[:=]\s*)(\S+)", RegexOptions.Compiled, MatchTimeout),
        new(@"(?i)(jwt[_-]?key\s*[:=]\s*)(\S+)", RegexOptions.Compiled, MatchTimeout),

        // Authorization: Bearer xxx
        new(@"(?i)(bearer\s+)[A-Za-z0-9._\-]+", RegexOptions.Compiled, MatchTimeout)
    ];

    /// <summary>对文本中的已知凭据模式做脱敏；空值原样返回。</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = text;
        foreach (var pattern in Patterns)
        {
            try
            {
                result = pattern.Replace(result, "$1***");
            }
            catch (RegexMatchTimeoutException)
            {
                // 极端长文本超时：放弃该条规则，避免日志写入被拖慢
            }
        }

        return result;
    }
}
