using System.Net;
using System.Text.Json;
using UserManagementSystem.DTOs;

namespace UserManagementSystem.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, code, message) = exception switch
        {
            ArgumentException => (HttpStatusCode.BadRequest, ResultCodes.ValidationError, exception.Message),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, ResultCodes.Unauthorized, exception.Message),
            KeyNotFoundException => (HttpStatusCode.NotFound, ResultCodes.NotFound, exception.Message),
            InvalidOperationException => (HttpStatusCode.BadRequest, ResultCodes.Error, exception.Message),
            _ => (HttpStatusCode.InternalServerError, ResultCodes.InternalServerError, "An unexpected error occurred")
        };

        context.Response.StatusCode = (int)statusCode;

        var response = new ApiResponse(code, message);
        var jsonResponse = JsonSerializer.Serialize(response);
        await context.Response.WriteAsync(jsonResponse);
    }
}
