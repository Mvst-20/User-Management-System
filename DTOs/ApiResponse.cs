using System.Text.Json.Serialization;

namespace UserManagementSystem.DTOs;

/// <summary>
/// 统一 API 响应格式
/// </summary>
public class ApiResponse
{
    /// <summary>
    /// 业务状态码
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// 响应消息
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 响应数据
    /// </summary>
    [JsonPropertyName("data")]
    public object? Data { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public ApiResponse() { }

    public ApiResponse(int code, string message, object? data = null)
    {
        Code = code;
        Message = message;
        Data = data;
    }
}

/// <summary>
/// 分页响应
/// </summary>
public class PagedApiResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    [JsonPropertyName("page")]
    public int Page { get; set; }
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("items")]
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
