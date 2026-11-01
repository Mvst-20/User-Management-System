using System.Net;
using System.Text.Json;
using UserManagementSystem.DTOs;
using UserManagementSystem.Exceptions;
using UserManagementSystem.Extensions;

namespace UserManagementSystem.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected / request aborted — not an application error, silently ignore
        }
        catch (BadHttpRequestException ex)
        {
            _logger.LogWarning(ex, "[BAD_REQUEST] {Method} {Path} from {IP} — {Message}",
                context.Request.Method, context.Request.Path,
                context.Connection.RemoteIpAddress, ex.Message);

            await TryWriteJsonError(context, (int)HttpStatusCode.BadRequest,
                new ApiResponse(ResultCodes.ValidationError, "请求格式错误"));
        }
        catch (BusinessException ex)
        {
            // 业务错误：服务层显式抛出，message 即面向客户端的业务文案
            _logger.LogWarning("[BUSINESS] {Method} {Path} | Code={Code} | {Message}",
                context.Request.Method, context.Request.Path, ex.Code, ex.Message);

            await TryWriteJsonError(context, ex.StatusCode,
                new ApiResponse(ex.Code, ex.Message));
        }
        catch (Exception ex)
        {
            // Extract request context for structured log
            var method = context.Request.Method;
            var path = context.Request.Path + context.Request.QueryString;
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userId = context.User?.GetUserId()?.ToString() ?? "anonymous";

            _logger.LogError(ex,
                "[UNHANDLED] {Method} {Path} | User={UserId} | IP={IP} | Type={ExceptionType} | {Message}",
                method, path, userId, ip, ex.GetType().Name, ex.Message);

            await HandleExceptionAsync(context, ex);
        }
    }

    /// <summary>
    /// 安全写入 JSON 错误响应。
    /// 如果响应已经开始（HasStarted），则跳过写入避免二次异常导致 HTML 错误页。
    /// </summary>
    private static async Task TryWriteJsonError(HttpContext context, int statusCode, ApiResponse response)
    {
        if (context.Response.HasStarted)
            return;

        try
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            var jsonResponse = JsonSerializer.Serialize(response);
            await context.Response.WriteAsync(jsonResponse);
        }
        catch (Exception writeEx)
        {
            // 写入响应失败（例如连接已断开），仅记录，不再抛出
            // 避免二次异常导致 ASP.NET 默认 HTML 错误页
            System.Diagnostics.Debug.WriteLine($"[ExceptionHandlingMiddleware] Failed to write error response: {writeEx.Message}");
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // 意外异常（内部 bug / 基础设施故障）统一处理；业务错误应使用 BusinessException
        var (statusCode, code, message) = exception switch
        {
            ArgumentException => (HttpStatusCode.BadRequest, ResultCodes.ValidationError, "请求参数错误"),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, ResultCodes.Unauthorized, "未授权"),
            KeyNotFoundException => (HttpStatusCode.NotFound, ResultCodes.NotFound, "资源不存在"),
            _ => (HttpStatusCode.InternalServerError, ResultCodes.InternalServerError, "服务器内部错误")
        };

        // 生产环境仅暴露通用错误信息，不泄露异常类型/内部细节；开发环境附带异常消息便于排查
        var displayMessage = _env.IsDevelopment()
            ? $"{message}: {exception.Message}"
            : message;

        await TryWriteJsonError(context, (int)statusCode,
            new ApiResponse(code, displayMessage));
    }
}
