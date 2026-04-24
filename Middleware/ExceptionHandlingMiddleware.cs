using System.Net;
using System.Text.Json;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred");
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

        // 开发环境返回详细错误信息，生产环境返回友好提示
        var displayMessage = _env.IsDevelopment() ? exception.Message : message;

        context.Response.StatusCode = (int)statusCode;

        var response = new ApiResponse(code, displayMessage);
        var jsonResponse = JsonSerializer.Serialize(response);
        await context.Response.WriteAsync(jsonResponse);
    }
}
