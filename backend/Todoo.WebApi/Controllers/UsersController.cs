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

    [HttpPost("me/photo")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadMyPhoto(IFormFile file)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new { success = false, message = "Dosya secilmedi." });
        }

        await using var stream = file.OpenReadStream();
        var result = await _userService.UploadProfilePhotoAsync(
            userId,
            file.FileName,
            file.ContentType,
            file.Length,
            stream);

        return result.ToActionResult();
    }

    [HttpDelete("me/photo")]
    public async Task<IActionResult> DeleteMyPhoto()
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        var result = await _userService.DeleteProfilePhotoAsync(userId);
        return result.ToActionResult();
    }

    [HttpGet("me/photo")]
    public async Task<IActionResult> GetMyPhoto()
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        return await DownloadPhotoInternal(userId, userId);
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

    [HttpGet("{id:int}/photo")]
    public async Task<IActionResult> GetPhoto(int id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        return await DownloadPhotoInternal(id, userId);
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

    private async Task<IActionResult> DownloadPhotoInternal(int targetUserId, int requesterUserId)
    {
        var result = await _userService.DownloadProfilePhotoAsync(targetUserId, requesterUserId);
        if (!result.Success)
        {
            return result.ToActionResult();
        }

        var (stream, contentType, fileName) = result.Data!;
        return File(stream, contentType, fileName);
    }
}
