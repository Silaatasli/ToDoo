using System.Net;
using Todoo.WebApi.Models;

namespace Todoo.WebApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Beklenmeyen bir hata olustu: {Path}", context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var response = new ErrorResponseDto
        {
            Success = false,
            StatusCode = StatusCodes.Status500InternalServerError,
            Message = _environment.IsDevelopment()
                ? exception.Message
                : "Beklenmeyen bir hata olustu."
        };

        if (_environment.IsDevelopment())
        {
            response.Detail = exception.StackTrace;
        }

        await context.Response.WriteAsJsonAsync(response);
    }
}
