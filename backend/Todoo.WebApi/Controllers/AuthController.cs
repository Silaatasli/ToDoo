using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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

    private bool TryGetUserId(out int userId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out userId);
    }

    [EnableRateLimiting("AuthRegister")]
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

    [EnableRateLimiting("AuthLogin")]
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

    [EnableRateLimiting("AuthRefresh")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequestDto request)
    {
        var result = await _authService.RefreshAsync(request.RefreshToken);
        if (!result.Success)
        {
            return Unauthorized(result);
        }

        return Ok(result);
    }

    [EnableRateLimiting("AuthForgotPassword")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto request)
    {
        var result = await _authService.ForgotPasswordAsync(request.Email);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [EnableRateLimiting("AuthForgotPassword")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto request)
    {
        var result = await _authService.ResetPasswordAsync(request.Token, request.NewPassword);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequestDto request)
    {
        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        await _authService.LogoutAsync(request.RefreshToken, jti);
        return Ok(new { success = true, message = "Cikis yapildi." });
    }

    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll()
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        await _authService.LogoutAllAsync(userId);
        return Ok(new { success = true, message = "Tum oturumlar sonlandirildi." });
    }
}
