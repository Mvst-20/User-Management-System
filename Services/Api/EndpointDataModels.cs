namespace UserManagementSystem.Services.Api;

/// <summary>
/// 服务端以匿名对象返回的 <c>data</c> 形状（无法复用强类型 DTO）。
/// 字段名遵循 JSON camelCase，反序列化时大小写不敏感。
/// </summary>
public sealed record OnlineUsersData(int OnlineUsers);

public sealed record TotalUsersData(int TotalUsers);

public sealed record NewUsersData(int NewUsers, int Days, string StartDate, string EndDate);

/// <summary>用户统计逐日趋势的单日数据（TotalUsers 为截至当日的累计值）。</summary>
public sealed record NewUsersDailyItem(
    string Date,
    int NewUsers,
    int TotalUsers,
    int ActiveUsers,
    int DeletedUsers);

/// <summary>用户统计逐日趋势（Items 已补零为完整序列；OnlineUsers 为当前快照）。</summary>
public sealed record NewUsersDailyData(
    int Days,
    string StartDate,
    string EndDate,
    int OnlineUsers,
    List<NewUsersDailyItem> Items);

public sealed record ActiveUsersData(int ActiveUsers, int Days, string StartDate, string EndDate);

/// <summary>服务器历史指标的单个降采样点。</summary>
public sealed record ServerMetricPoint(
    string Time,
    double CpuUsage,
    double MemoryUsage,
    double MemoryUsedMb,
    double NetworkInBps,
    double NetworkOutBps);

/// <summary>服务器历史指标序列（已按范围分桶降采样）。</summary>
public sealed record ServerMetricsData(
    int Hours,
    int BucketMinutes,
    List<ServerMetricPoint> Items);

/// <summary>运行日志的单条记录（来自内存环形缓冲）。</summary>
public sealed record RuntimeLogEntry(
    long Sequence,
    string Timestamp,
    string Level,
    string Message,
    string? Exception);

/// <summary>运行日志增量拉取结果。</summary>
public sealed record RuntimeLogsData(
    List<RuntimeLogEntry> Items,
    long LastSequence);
