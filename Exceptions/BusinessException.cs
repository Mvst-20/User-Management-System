namespace UserManagementSystem.Exceptions;

/// <summary>
/// 业务错误异常：由服务层显式抛出，表示"客户端请求本身不满足业务规则"，
/// 由端点 catch 或全局中间件转换为业务响应。
/// 与意外异常（内部 bug、基础设施故障，统一按 500 处理并隐藏细节）明确区分。
/// </summary>
public class BusinessException : Exception
{
    /// <summary>业务响应码（对应 ApiResponse.Code，取值见 ResultCodes）</summary>
    public int Code { get; }

    /// <summary>HTTP 状态码</summary>
    public int StatusCode { get; }

    public BusinessException(int code, string message, int statusCode = StatusCodes.Status400BadRequest)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }
}
