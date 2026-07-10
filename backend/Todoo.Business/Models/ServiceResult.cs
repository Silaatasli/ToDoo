namespace Todoo.Business.Models;

public enum ServiceErrorKind
{
    Validation = 0,
    NotFound = 1,
    Forbidden = 2
}

public class ServiceResult<T>
{
    public bool Success { get; init; }

    public T? Data { get; init; }

    public string? ErrorMessage { get; init; }

    public ServiceErrorKind? ErrorKind { get; init; }

    public static ServiceResult<T> Ok(T data) => new() { Success = true, Data = data };

    public static ServiceResult<T> Fail(string message, ServiceErrorKind kind = ServiceErrorKind.Validation) =>
        new() { Success = false, ErrorMessage = message, ErrorKind = kind };
}

public class ServiceResult
{
    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public ServiceErrorKind? ErrorKind { get; init; }

    public static ServiceResult Ok() => new() { Success = true };

    public static ServiceResult Fail(string message, ServiceErrorKind kind = ServiceErrorKind.Validation) =>
        new() { Success = false, ErrorMessage = message, ErrorKind = kind };
}
