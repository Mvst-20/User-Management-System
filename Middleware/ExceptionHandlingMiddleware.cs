using System.Net;
using System.Text.Json;

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

        var response = exception switch
        {
            ArgumentException => new { StatusCode = HttpStatusCode.BadRequest, Message = exception.Message },
            UnauthorizedAccessException => new { StatusCode = HttpStatusCode.Unauthorized, Message = exception.Message },
            KeyNotFoundException => new { StatusCode = HttpStatusCode.NotFound, Message = exception.Message },
            InvalidOperationException => new { StatusCode = HttpStatusCode.BadRequest, Message = exception.Message },
            _ => new { StatusCode = HttpStatusCode.InternalServerError, Message = "An unexpected error occurred" }
        };

        context.Response.StatusCode = (int)response.StatusCode;

        var jsonResponse = JsonSerializer.Serialize(new { error = response.Message });
        await context.Response.WriteAsync(jsonResponse);
    }
}
