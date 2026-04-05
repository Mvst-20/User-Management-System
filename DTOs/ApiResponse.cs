using System.Text.Json;

namespace UserManagementSystem.DTOs;

/// <summary>
/// 统一 API 响应格式
/// </summary>
public class ApiResponse
{
    /// <summary>
    /// 业务状态码
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// 响应消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 响应数据
    /// </summary>
    public object? Data { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public ApiResponse() { }

    public ApiResponse(int code, string message, object? data = null)
    {
        Code = code;
        Message = message;
        Data = data;
    }

    // ============ 静态工厂方法 ============

    public static ApiResponse Success(string message = "操作成功", object? data = null) =>
        new(ResultCodes.Success, message, data);

    public static ApiResponse Success(int code, string message, object? data = null) =>
        new(code, message, data);

    public static ApiResponse Error(int code, string message) =>
        new(code, message);

    public static ApiResponse Error(string message) =>
        new(ResultCodes.Error, message);
}

/// <summary>
/// 分页响应
/// </summary>
public class PagedApiResponse
{
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<object> Items { get; set; } = new();

    public PagedApiResponse() { }

    public PagedApiResponse(int code, string message, List<object> items, int page, int pageSize, int totalCount)
    {
        Code = code;
        Message = message;
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
        TotalPages = (int)Math.Ceiling((double)totalCount / pageSize);
    }
}
