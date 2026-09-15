namespace UserManagementSystem.Services.Api;

/// <summary>
/// 管理前端调用本进程 REST API 的统一结果。
/// 成功以 HTTP 2xx 判定；失败时保留服务端业务码与消息以便提示。
/// </summary>
/// <typeparam name="T">响应数据（对应服务端 ApiResponse.data）</typeparam>
public sealed record ApiResult<T>(bool Success, int Code, string Message, T? Data)
{
    public static ApiResult<T> Ok(T? data, string message = "操作成功", int code = 0)
        => new(true, code, message, data);

    public static ApiResult<T> Fail(int code, string message)
        => new(false, code, message, default);
}

/// <summary>
/// 分页结果（对应服务端 PagedApiResponse）。
/// </summary>
public sealed record PagedResult<T>(
    bool Success,
    int Code,
    string Message,
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public static PagedResult<T> Ok(IReadOnlyList<T> items, int page, int pageSize, int totalCount, string message = "获取成功", int code = 0)
        => new(true, code, message, items, page, pageSize, totalCount);

    public static PagedResult<T> Fail(int code, string message)
        => new(false, code, message, Array.Empty<T>(), 1, 20, 0);
}
