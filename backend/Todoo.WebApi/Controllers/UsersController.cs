using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Todoo.Business.Abstract;
using Todoo.WebApi.Helpers;
using Todoo.WebApi.Models.Users;

namespace Todoo.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    private bool TryGetUserId(out int userId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out userId);
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _userService.GetOwnProfileAsync(userId);
        return result.ToActionResult();
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequestDto request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _userService.UpdateProfileAsync(
            userId,
            request.FirstName,
            request.LastName,
            request.PhoneNumber,
            request.Title);

        return result.ToActionResult();
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _userService.SearchUsersAsync(q, userId);
        return result.ToActionResult();
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _userService.GetProfileAsync(id, userId);
        return result.ToActionResult();
    }
}
