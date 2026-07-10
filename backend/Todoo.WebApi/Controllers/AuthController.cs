using Microsoft.AspNetCore.Mvc;
using Todoo.Business.Abstract;
using Todoo.WebApi.Models.Auth;

namespace Todoo.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
    {
        var result = await _authService.RegisterAsync(
            request.FirstName,
            request.LastName,
            request.Email,
            request.Password);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        var result = await _authService.LoginAsync(request.Email, request.Password);
        if (!result.Success)
        {
            return Unauthorized(result);
        }

        return Ok(result);
    }
}
