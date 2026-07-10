namespace Todoo.WebApi.Models;

public class ErrorResponseDto
{
    public bool Success { get; set; }

    public int StatusCode { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? Detail { get; set; }
}
