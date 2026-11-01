namespace UserManagementSystem.Configuration;

/// <summary>
/// 数据库连接串解析：连接串不随代码入库，由系统（环境）变量提供。
/// <para>
/// 解析优先级：
/// <list type="number">
///   <item>环境变量 <c>UCS_DB_CONNECTION</c>（推荐）</item>
///   <item>配置键 <c>ConnectionStrings:Default</c>，其环境变量形式为
///         <c>ConnectionStrings__Default</c>（ASP.NET Core 标准约定）</item>
/// </list>
/// </para>
/// <para>
/// 取值不做任何静默修正：发现明显错误（如首尾带引号）时直接抛出明确异常，
/// 避免把错误配置带着跑，也避免"关键字不受支持"这类难以定位的驱动层报错。
/// </para>
/// </summary>
public static class DatabaseConnection
{
    /// <summary>推荐使用的环境变量名。</summary>
    public const string EnvironmentVariableName = "UCS_DB_CONNECTION";

    /// <summary>备用配置键（环境变量形式：<c>ConnectionStrings__Default</c>）。</summary>
    public const string ConfigurationKey = "ConnectionStrings:Default";

    /// <summary>可能被误粘贴的引号字符（半角与全角）。</summary>
    private static readonly char[] QuoteCharacters =
        ['"', '\'', '“', '”', '‘', '’', '「', '」', '『', '』'];

    /// <summary>解析数据库连接串；缺失或明显非法时抛出可读的启动异常。</summary>
    public static string Resolve(IConfiguration configuration)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return NormalizeCredentials(Validate(fromEnvironment.Trim(), $"{EnvironmentVariableName}（环境变量）"));
        }

        var fromConfiguration = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(fromConfiguration))
        {
            return NormalizeCredentials(Validate(fromConfiguration.Trim(), $"{ConfigurationKey}（配置键）"));
        }

        throw new InvalidOperationException(BuildMissingMessage());
    }

    /// <summary>
    /// 把形如 <c>server=user:password@host</c> 的连接串规范化为
    /// <c>server=host;user=user;password=password</c>。
    /// <para>
    /// 这是避免凭据泄露的关键：驱动会把 <c>Server</c> 原样作为 <c>DbConnection.DataSource</c>，
    /// 而 EF Core 的 <c>ConnectionError</c> 日志会打印该值。若不在入口拆分，
    /// 连接失败时明文口令就会出现在日志中。显式提供的 <c>user</c>/<c>password</c>
    /// 关键字优先，不会被覆盖。
    /// </para>
    /// </summary>
    public static string NormalizeCredentials(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || !connectionString.Contains('@'))
        {
            return connectionString;
        }

        var segments = connectionString.Split(';', StringSplitOptions.TrimEntries);
        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in segments)
        {
            var pair = segment.Split('=', 2);
            if (pair.Length == 2)
            {
                existingKeys.Add(pair[0].Trim());
            }
        }

        var rebuilt = new List<string>(segments.Length + 2);
        string? userFromServer = null;
        string? passwordFromServer = null;

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            var pair = segment.Split('=', 2);
            if (pair.Length != 2)
            {
                rebuilt.Add(segment);
                continue;
            }

            var key = pair[0].Trim();
            var value = pair[1].Trim();

            if (IsServerKey(key) && value.Contains('@'))
            {
                var at = value.LastIndexOf('@');
                var userInfo = value[..at];
                var host = value[(at + 1)..];
                var colon = userInfo.IndexOf(':');

                if (host.Length > 0 && colon > 0)
                {
                    userFromServer = userInfo[..colon];
                    passwordFromServer = userInfo[(colon + 1)..];
                    rebuilt.Add($"{key}={host}");
                    continue;
                }
            }

            rebuilt.Add($"{key}={value}");
        }

        if (!string.IsNullOrEmpty(userFromServer) &&
            !existingKeys.Contains("user") &&
            !existingKeys.Contains("userid") &&
            !existingKeys.Contains("user id") &&
            !existingKeys.Contains("username"))
        {
            rebuilt.Add($"user={userFromServer}");
        }

        if (!string.IsNullOrEmpty(passwordFromServer) &&
            !existingKeys.Contains("password") &&
            !existingKeys.Contains("pwd"))
        {
            rebuilt.Add($"password={passwordFromServer}");
        }

        return string.Join(';', rebuilt);
    }

    private static bool IsServerKey(string key)
        => key.Equals("server", StringComparison.OrdinalIgnoreCase)
           || key.Equals("host", StringComparison.OrdinalIgnoreCase)
           || key.Equals("data source", StringComparison.OrdinalIgnoreCase)
           || key.Equals("datasource", StringComparison.OrdinalIgnoreCase)
           || key.Equals("address", StringComparison.OrdinalIgnoreCase)
           || key.Equals("addr", StringComparison.OrdinalIgnoreCase)
           || key.Equals("network address", StringComparison.OrdinalIgnoreCase);

    /// <summary>连接串来源标识，用于启动日志。</summary>
    public static string DescribeSource()
        => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariableName))
            ? $"config:{ConfigurationKey}"
            : $"env:{EnvironmentVariableName}";

    /// <summary>
    /// 生成不含凭据的连接串摘要（仅保留 server / database），用于启动日志。
    /// </summary>
    public static string DescribeTarget(string connectionString)
    {
        var parts = ParseSegments(connectionString);

        var host = parts.TryGetValue("server", out var server) ? server
            : parts.TryGetValue("host", out var h) ? h
            : "?";

        // 兼容 server=user:password@host 形式，只保留主机部分
        var at = host.LastIndexOf('@');
        if (at >= 0)
        {
            host = host[(at + 1)..];
        }

        var database = parts.TryGetValue("database", out var db) ? db : "?";
        return $"{host}/{database}";
    }

    /// <summary>
    /// 拒绝明显错误的取值（不修正）。当前仅校验首尾引号——这是从聊天窗口或文档
    /// 粘贴到「系统属性 → 环境变量」时最常带入的错误，若不拦截会表现为驱动层
    /// 报"关键字不受支持"，错误信息难以指向真正原因。
    /// </summary>
    private static string Validate(string connectionString, string source)
    {
        var first = connectionString[0];
        var last = connectionString[^1];

        if (QuoteCharacters.Contains(first) || QuoteCharacters.Contains(last))
        {
            throw new InvalidOperationException(
                $"{source} 的取值首尾包含引号（实际以「{first}」开头、以「{last}」结尾，长度 {connectionString.Length}）。" +
                "连接串必须是不带任何引号的纯文本，请去掉引号后重试。" +
                "注意：从聊天窗口或网页复制时容易带入全角引号“ ”。");
        }

        return connectionString;
    }

    /// <summary>
    /// 未配置连接串时的诊断信息：列出已检测到的相关变量名及其层级，并给出排查要点。
    /// </summary>
    private static string BuildMissingMessage()
    {
        var related = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in new[]
                 {
                     EnvironmentVariableTarget.Process,
                     EnvironmentVariableTarget.User,
                     EnvironmentVariableTarget.Machine
                 })
        {
            try
            {
                foreach (var key in Environment.GetEnvironmentVariables(target).Keys)
                {
                    var name = key.ToString();
                    if (!string.IsNullOrWhiteSpace(name) &&
                        (name.Contains("CONNECTION", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("UCS_", StringComparison.OrdinalIgnoreCase)))
                    {
                        related.Add($"{name}({target})");
                    }
                }
            }
            catch
            {
                // 读取某一层级失败不影响诊断主流程
            }
        }

        var detected = related.Count > 0 ? string.Join(", ", related) : "（无）";

        return $"""
            未配置数据库连接串。
              · 期望来源：环境变量 {EnvironmentVariableName}，或配置键 {ConfigurationKey}（环境变量 ConnectionStrings__Default）
              · 当前已检测到的相关变量：{detected}
              · 若已在「系统属性 → 环境变量」中设置，请确认：① 变量名无多余空格；② 取值不带任何引号；③ 设置后必须重新打开终端（或重启应用）才会生效。
              · 取值示例：server=127.0.0.1;database=ucsdb;user=ucs;password=your_password;CharSet=utf8mb4;
            """;
    }

    /// <summary>把连接串拆成 键 → 值（键大小写不敏感，重复键保留首个）。</summary>
    private static Dictionary<string, string> ParseSegments(string connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = segment.Split('=', 2);
            if (pair.Length == 2)
            {
                result.TryAdd(pair[0].Trim(), pair[1].Trim());
            }
        }

        return result;
    }
}
