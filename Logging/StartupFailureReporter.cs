using System.Text;

namespace UserManagementSystem.Logging;

/// <summary>
/// 启动失败的兜底上报：把失败原因输出到标准错误并追加到文件。
/// <para>
/// 不依赖 Serilog —— 日志系统本身可能未成功初始化，此时该路径仍能留下证据。
/// 不做任何阻塞：不会等待按键、不会占用终端，服务与容器环境同样适用。
/// </para>
/// </summary>
public static class StartupFailureReporter
{
    /// <summary>上报启动失败。</summary>
    /// <param name="exception">导致启动失败的异常。</param>
    /// <param name="note">附加说明（可选）。</param>
    public static void Report(Exception exception, string? note = null)
    {
        var report = Build(exception, note);

        WriteToConsole(report);
        WriteToFile(report);
    }

    private static string Build(Exception exception, string? note)
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("══════════════════════════════════════════════════════════════");
        builder.AppendLine("  UCS 启动失败");
        builder.AppendLine("══════════════════════════════════════════════════════════════");
        builder.AppendLine($"  时间：      {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"  工作目录：  {SafeCurrentDirectory()}");
        builder.AppendLine($"  异常类型：  {exception.GetType().FullName}");
        builder.AppendLine($"  异常信息：  {exception.Message}");

        if (exception.InnerException is { } inner)
        {
            builder.AppendLine($"  内部异常：  {inner.GetType().FullName}: {inner.Message}");
        }

        if (!string.IsNullOrWhiteSpace(note))
        {
            builder.AppendLine($"  备注：      {note}");
        }

        builder.AppendLine();
        builder.AppendLine("  堆栈：");
        builder.AppendLine(exception.StackTrace ?? "（无）");
        builder.AppendLine("──────────────────────────────────────────────────────────────");
        return builder.ToString();
    }

    private static void WriteToConsole(string report)
    {
        try
        {
            Console.Error.WriteLine(report);
        }
        catch
        {
            // 无控制台时忽略
        }
    }

    /// <summary>追加到文件，逐个尝试候选路径，全部失败则放弃（不影响退出流程）。</summary>
    private static void WriteToFile(string report)
    {
        foreach (var candidate in CandidatePaths())
        {
            try
            {
                var directory = Path.GetDirectoryName(candidate);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(candidate, report, Encoding.UTF8);
                return;
            }
            catch
            {
                // 尝试下一个候选位置
            }
        }
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var workingDirectory = SafeCurrentDirectory();
        yield return Path.Combine(workingDirectory, "logs", "startup-error.log");
        yield return Path.Combine(workingDirectory, "startup-error.log");
        yield return Path.Combine(Path.GetTempPath(), "ucs-startup-error.log");
    }

    private static string SafeCurrentDirectory()
    {
        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch
        {
            return "（未知）";
        }
    }
}
