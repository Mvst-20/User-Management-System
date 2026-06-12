using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using UserManagementSystem.DTOs;

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
        catch (OperationCanceledException)
        {
            // Client disconnected / request aborted — not an application error
        }
        catch (BadHttpRequestException ex)
        {
            _logger.LogWarning(ex, "[BAD_REQUEST] {Method} {Path} from {IP} — {Message}",
                context.Request.Method, context.Request.Path,
                context.Connection.RemoteIpAddress, ex.Message);

            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = "application/json";
            var response = new ApiResponse(ResultCodes.ValidationError, "请求格式错误");
            var json = JsonSerializer.Serialize(response, AppJsonSerializerContext.Default.ApiResponse);
            await context.Response.WriteAsync(json);
        }
        catch (Exception ex)
        {
            // Extract request context for structured log
            var method = context.Request.Method;
            var path = context.Request.Path + context.Request.QueryString;
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userId = context.User?.FindFirst("uid")?.Value
                ?? context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? "anonymous";

            _logger.LogError(ex,
                "[UNHANDLED] {Method} {Path} | User={UserId} | IP={IP} | Type={ExceptionType} | {Message}",
                method, path, userId, ip, ex.GetType().Name, ex.Message);

            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, code, message) = exception switch
        {
            ArgumentException => (HttpStatusCode.BadRequest, ResultCodes.ValidationError, exception.Message),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, ResultCodes.Unauthorized, "未授权"),
            KeyNotFoundException => (HttpStatusCode.NotFound, ResultCodes.NotFound, "资源不存在"),
            InvalidOperationException => (HttpStatusCode.BadRequest, ResultCodes.Error, "请求处理失败"),
            _ => (HttpStatusCode.InternalServerError, ResultCodes.InternalServerError, "服务器内部错误")
        };

        var displayMessage = _env.IsDevelopment() ? exception.Message : message;
        context.Response.StatusCode = (int)statusCode;

        var response = new ApiResponse(code, displayMessage);
        var jsonResponse = JsonSerializer.Serialize(response, AppJsonSerializerContext.Default.ApiResponse);
        await context.Response.WriteAsync(jsonResponse);
    }
}
