using Serilog.Core;
using Serilog.Events;

namespace UserManagementSystem.Logging;

/// <summary>单条内存日志（运行日志页的数据源）。</summary>
public sealed record MemoryLogEntry(
    long Sequence,
    DateTime Timestamp,
    string Level,
    string Message,
    string? Exception);

/// <summary>
/// Serilog 内存环形缓冲 Sink：
/// <para>
/// 固定容量（约 2000 条）防止内存膨胀；每条带进程内单调递增序号，
/// 运行日志端点按 <c>afterId</c> 做增量拉取。进程重启后序号从 1 开始（内存态，符合"实时日志"定位，
/// 历史日志仍以 logs/ 目录文件为准）。
/// </para>
/// </summary>
public sealed class MemoryLogSink : ILogEventSink
{
    /// <summary>进程级单例：bootstrap logger 与运行日志端点共享同一份缓冲。</summary>
    public static MemoryLogSink Instance { get; } = new();

    private const int Capacity = 2000;

    private readonly object _lock = new();
    private readonly Queue<MemoryLogEntry> _buffer = new(Capacity);
    private long _sequence;

    public void Emit(LogEvent logEvent)
    {
        var entry = new MemoryLogEntry(
            Interlocked.Increment(ref _sequence),
            logEvent.Timestamp.LocalDateTime,
            logEvent.Level.ToString(),
            logEvent.RenderMessage(),
            logEvent.Exception?.ToString());

        lock (_lock)
        {
            _buffer.Enqueue(entry);
            while (_buffer.Count > Capacity)
            {
                _buffer.Dequeue();
            }
        }
    }

    /// <summary>
    /// 读取 <paramref name="afterSequence"/> 之后的日志（按序号升序）。
    /// </summary>
    /// <param name="afterSequence">上次读取到的最大序号；0 表示从头。</param>
    /// <param name="level">级别过滤（Debug/Information/Warning/Error/Fatal）；null 或空表示不过滤。</param>
    public IReadOnlyList<MemoryLogEntry> ReadAfter(long afterSequence, string? level = null)
    {
        lock (_lock)
        {
            IEnumerable<MemoryLogEntry> query = _buffer;

            if (afterSequence > 0)
            {
                query = query.Where(entry => entry.Sequence > afterSequence);
            }

            if (!string.IsNullOrWhiteSpace(level))
            {
                query = query.Where(entry =>
                    string.Equals(entry.Level, level.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            return query.OrderBy(entry => entry.Sequence).ToList();
        }
    }
}
