using Microsoft.AspNetCore.Mvc;
using Todoo.Business.Models;

namespace Todoo.WebApi.Helpers;

public static class ServiceResultExtensions
{
    public static IActionResult ToActionResult<T>(this ServiceResult<T> result, Func<T, IActionResult>? onSuccess = null)
    {
        if (result.Success)
        {
            return onSuccess is not null ? onSuccess(result.Data!) : new OkObjectResult(result.Data);
        }

        return ToErrorResult(result.ErrorMessage, result.ErrorKind);
    }

    public static IActionResult ToActionResult(this ServiceResult result, Func<IActionResult>? onSuccess = null)
    {
        if (result.Success)
        {
            return onSuccess is not null ? onSuccess() : new NoContentResult();
        }

        return ToErrorResult(result.ErrorMessage, result.ErrorKind);
    }

    private static IActionResult ToErrorResult(string? message, ServiceErrorKind? kind)
    {
        var body = new { success = false, message };

        return kind switch
        {
            ServiceErrorKind.NotFound => new NotFoundObjectResult(body),
            ServiceErrorKind.Forbidden => new ObjectResult(body) { StatusCode = StatusCodes.Status403Forbidden },
            _ => new BadRequestObjectResult(body)
        };
    }
}
