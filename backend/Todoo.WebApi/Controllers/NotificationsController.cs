using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Todoo.Business.Abstract;

namespace Todoo.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationStore _notificationStore;

    public NotificationsController(INotificationStore notificationStore)
    {
        _notificationStore = notificationStore;
    }

    private bool TryGetUserId(out int userId)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out userId);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = 30)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var items = await _notificationStore.ListAsync(userId, take);
        var unread = await _notificationStore.GetUnreadCountAsync(userId);
        return Ok(new { items, unreadCount = unread });
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var unread = await _notificationStore.GetUnreadCountAsync(userId);
        return Ok(new { unreadCount = unread });
    }

    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkRead(string id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var ok = await _notificationStore.MarkReadAsync(userId, id);
        if (!ok)
        {
            return NotFound(new { success = false, message = "Bildirim bulunamadı." });
        }

        var unread = await _notificationStore.GetUnreadCountAsync(userId);
        return Ok(new { success = true, unreadCount = unread });
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        await _notificationStore.MarkAllReadAsync(userId);
        return Ok(new { success = true, unreadCount = 0 });
    }

    [HttpPost("read-many")]
    public async Task<IActionResult> MarkReadMany([FromBody] NotificationIdsRequest request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        await _notificationStore.MarkReadManyAsync(userId, request.Ids ?? []);
        var unread = await _notificationStore.GetUnreadCountAsync(userId);
        return Ok(new { success = true, unreadCount = unread });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var ok = await _notificationStore.DeleteAsync(userId, id);
        if (!ok)
        {
            return NotFound(new { success = false, message = "Bildirim bulunamadı." });
        }

        var unread = await _notificationStore.GetUnreadCountAsync(userId);
        return Ok(new { success = true, unreadCount = unread });
    }

    [HttpPost("delete-many")]
    public async Task<IActionResult> DeleteMany([FromBody] NotificationIdsRequest request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var deleted = await _notificationStore.DeleteManyAsync(userId, request.Ids ?? []);
        var unread = await _notificationStore.GetUnreadCountAsync(userId);
        return Ok(new { success = true, deleted, unreadCount = unread });
    }

    [HttpPost("clear")]
    public async Task<IActionResult> Clear()
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { success = false, message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        await _notificationStore.ClearAsync(userId);
        return Ok(new { success = true, unreadCount = 0 });
    }
}

public sealed class NotificationIdsRequest
{
    public string[]? Ids { get; set; }
}
