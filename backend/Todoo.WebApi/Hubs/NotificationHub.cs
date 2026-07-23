using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Todoo.Business.Abstract;

namespace Todoo.WebApi.Hubs;

/// <summary>
/// Bildirim hub'i: kisi grubu (user-{id}) + takim gruplari (team-{id}).
/// Kisisel bildirimler user grubuna, takim broadcast'leri team grubuna gider.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    private readonly ITeamService _teamService;
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(ITeamService teamService, ILogger<NotificationHub> logger) 
    {
        _teamService = teamService;
        _logger = logger;
    }

    public static string UserGroup(int userId) => $"user-{userId}";

    public static string TeamGroup(int teamId) => $"team-{teamId}";

    public override async Task OnConnectedAsync()
    {
        if (TryGetUserId(out var userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
            await JoinUserTeamsAsync(userId);
            _logger.LogDebug(
                "NotificationHub baglandi. UserId={UserId}, ConnectionId={ConnectionId}",
                userId,
                Context.ConnectionId);
        }
        else
        {
            _logger.LogWarning(
                "NotificationHub baglandi ama kullanici kimligi okunamadi. ConnectionId={ConnectionId}",
                Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Group uyelikleri connection kopunca otomatik temizlenir.
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Yeni takima eklenince client cagirir.</summary>
    public async Task JoinTeam(int teamId)
    {
        if (!TryGetUserId(out var userId))
        {
            throw new HubException("Gecerli bir kullanici bilgisi bulunamadi.");
        }

        if (!await _teamService.IsTeamMemberAsync(teamId, userId))
        {
            throw new HubException("Bu takimin uyesi degilsiniz.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TeamGroup(teamId));
    }

    public async Task LeaveTeam(int teamId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, TeamGroup(teamId));
    }

    /// <summary>Reconnect veya uyelik degisimi sonrasi takim gruplarini yeniler.</summary>
    public async Task RefreshTeamGroups()
    {
        if (!TryGetUserId(out var userId))
        {
            throw new HubException("Gecerli bir kullanici bilgisi bulunamadi.");
        }

        await JoinUserTeamsAsync(userId);
    }

    private async Task JoinUserTeamsAsync(int userId)
    {
        var teamIds = await _teamService.GetTeamIdsForUserAsync(userId);
        foreach (var teamId in teamIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TeamGroup(teamId));
        }

        _logger.LogDebug(
            "NotificationHub takim gruplarina eklendi. UserId={UserId}, TeamCount={Count}",
            userId,
            teamIds.Count);
    }

    private bool TryGetUserId(out int userId)
    {
        var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub");
        return int.TryParse(raw, out userId);
    }
}
