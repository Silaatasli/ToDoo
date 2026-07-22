using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Todoo.WebApi.Hubs;

/// <summary>
/// SignalR Clients.User(...) icin kullanici kimligini JWT NameIdentifier / sub claim'inden okur.
/// </summary>
public sealed class AppUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        var user = connection.User;
        if (user is null)
        {
            return null;
        }

        return user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");
    }
}
