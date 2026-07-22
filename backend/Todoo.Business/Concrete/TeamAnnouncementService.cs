using Todoo.Business.Abstract;
using Todoo.Business.Helpers;
using Todoo.Business.Models;
using Todoo.Business.Models.Teams;
using Todoo.DataAccess.UnitOfWork;
using Todoo.Entities.Entities;
using Todoo.Entities.Enums;

namespace Todoo.Business.Concrete;

public class TeamAnnouncementService : ITeamAnnouncementService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITeamService _teamService;
    private readonly NotificationDispatchService _notificationDispatch;
    private readonly ITeamBoardNotifier _boardNotifier;

    public TeamAnnouncementService(
        IUnitOfWork unitOfWork,
        ITeamService teamService,
        NotificationDispatchService notificationDispatch,
        ITeamBoardNotifier boardNotifier)
    {
        _unitOfWork = unitOfWork;
        _teamService = teamService;
        _notificationDispatch = notificationDispatch;
        _boardNotifier = boardNotifier;
    }

    public async Task<ServiceResult<IEnumerable<TeamAnnouncementDto>>> ListAsync(int teamId, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult<IEnumerable<TeamAnnouncementDto>>.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await _teamService.IsTeamMemberAsync(teamId, userId))
        {
            return ServiceResult<IEnumerable<TeamAnnouncementDto>>.Fail(
                "Bu takimin uyesi degilsiniz.",
                ServiceErrorKind.Forbidden);
        }

        var canManage = await CanPublishAsync(team, userId);
        var users = (await _unitOfWork.Users.GetAllAsync()).ToDictionary(user => user.Id);
        var items = (await _unitOfWork.TeamAnnouncements.GetAllAsync())
            .Where(item => item.TeamId == teamId)
            .Where(item => canManage || item.Status == AnnouncementStatus.Published)
            .OrderByDescending(item => item.PublishedAt ?? item.ScheduledPublishAt ?? item.CreatedDate)
            .Select(item => MapToDto(item, users.GetValueOrDefault(item.AuthorUserId)))
            .ToList();

        return ServiceResult<IEnumerable<TeamAnnouncementDto>>.Ok(items);
    }

    public async Task<ServiceResult<TeamAnnouncementDto>> CreateAsync(
        int teamId,
        string title,
        string body,
        string publishMode,
        DateTime? scheduledPublishAt,
        int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult<TeamAnnouncementDto>.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await CanPublishAsync(team, userId))
        {
            return ServiceResult<TeamAnnouncementDto>.Fail(
                "Duyuru yayinlama yetkiniz yok.",
                ServiceErrorKind.Forbidden);
        }

        var contentResult = ValidateContent(title, body);
        if (!contentResult.Success)
        {
            return ServiceResult<TeamAnnouncementDto>.Fail(contentResult.ErrorMessage!);
        }

        var mode = NormalizePublishMode(publishMode);
        if (mode is null)
        {
            return ServiceResult<TeamAnnouncementDto>.Fail("Gecersiz yayinlama modu. Draft, Now veya Schedule kullanin.");
        }

        DateTime? scheduledUtc = null;
        DateTime? publishedAt = null;
        var status = AnnouncementStatus.Draft;

        if (mode == "Now")
        {
            status = AnnouncementStatus.Published;
            publishedAt = DateTime.UtcNow;
        }
        else if (mode == "Schedule")
        {
            if (!scheduledPublishAt.HasValue)
            {
                return ServiceResult<TeamAnnouncementDto>.Fail("Zamanlanmis duyuru icin yayin tarihi zorunludur.");
            }

            scheduledUtc = ToUtc(scheduledPublishAt.Value);
            if (scheduledUtc <= DateTime.UtcNow.AddMinutes(-1))
            {
                return ServiceResult<TeamAnnouncementDto>.Fail("Yayin tarihi gelecekte olmalidir.");
            }

            status = AnnouncementStatus.Scheduled;
        }
        else if (mode == "Draft")
        {
            // Taslak sadece Draft; zamanlama ayri Schedule modu ile yapilir.
            if (scheduledPublishAt.HasValue)
            {
                return ServiceResult<TeamAnnouncementDto>.Fail(
                    "Taslak duyuruya yayin tarihi eklenemez. Zamanlamak icin Schedule modunu kullanin.");
            }

            status = AnnouncementStatus.Draft;
        }

        var announcement = new TeamAnnouncement
        {
            TeamId = teamId,
            AuthorUserId = userId,
            Title = title.Trim(),
            Body = body.Trim(),
            Status = status,
            ScheduledPublishAt = scheduledUtc,
            PublishedAt = publishedAt
        };

        _unitOfWork.TeamAnnouncements.Add(announcement);
        await _unitOfWork.SaveChangesAsync();

        if (status == AnnouncementStatus.Published)
        {
            await NotifyPublishedAsync(teamId, userId, announcement, includeActor: false);
        }

        var author = await _unitOfWork.Users.GetByIdAsync(userId);
        return ServiceResult<TeamAnnouncementDto>.Ok(MapToDto(announcement, author));
    }

    public async Task<ServiceResult<TeamAnnouncementDto>> PublishAsync(int teamId, int announcementId, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult<TeamAnnouncementDto>.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await CanPublishAsync(team, userId))
        {
            return ServiceResult<TeamAnnouncementDto>.Fail(
                "Duyuru yayinlama yetkiniz yok.",
                ServiceErrorKind.Forbidden);
        }

        var announcement = await _unitOfWork.TeamAnnouncements.GetByIdAsync(announcementId);
        if (announcement is null || announcement.TeamId != teamId)
        {
            return ServiceResult<TeamAnnouncementDto>.Fail("Duyuru bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (announcement.Status == AnnouncementStatus.Published)
        {
            return ServiceResult<TeamAnnouncementDto>.Fail("Duyuru zaten yayinlanmis.");
        }

        announcement.Status = AnnouncementStatus.Published;
        announcement.PublishedAt = DateTime.UtcNow;
        announcement.ScheduledPublishAt = null;
        _unitOfWork.TeamAnnouncements.Update(announcement);
        await _unitOfWork.SaveChangesAsync();

        await NotifyPublishedAsync(teamId, announcement.AuthorUserId, announcement, includeActor: false);

        var author = await _unitOfWork.Users.GetByIdAsync(announcement.AuthorUserId);
        return ServiceResult<TeamAnnouncementDto>.Ok(MapToDto(announcement, author));
    }

    public async Task<ServiceResult> DeleteAsync(int teamId, int announcementId, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await _teamService.IsTeamMemberAsync(teamId, userId))
        {
            return ServiceResult.Fail("Bu takimin uyesi degilsiniz.", ServiceErrorKind.Forbidden);
        }

        var announcement = await _unitOfWork.TeamAnnouncements.GetByIdAsync(announcementId);
        if (announcement is null || announcement.TeamId != teamId)
        {
            return ServiceResult.Fail("Duyuru bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (announcement.AuthorUserId != userId && team.LeaderUserId != userId)
        {
            return ServiceResult.Fail("Bu duyuruyu silme yetkiniz yok.", ServiceErrorKind.Forbidden);
        }

        await _unitOfWork.TeamAnnouncements.DeleteAsync(announcement.Id);
        await _unitOfWork.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SetPublishPermissionAsync(
        int teamId,
        int memberUserId,
        bool canPublish,
        int actorUserId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (team.LeaderUserId != actorUserId)
        {
            return ServiceResult.Fail("Sadece lider duyuru yayinlama yetkisi verebilir.", ServiceErrorKind.Forbidden);
        }

        if (memberUserId == team.LeaderUserId)
        {
            return ServiceResult.Fail("Lider zaten duyuru yayinlayabilir.");
        }

        var membership = (await _unitOfWork.TeamMembers.GetAllAsync())
            .FirstOrDefault(member => member.TeamId == teamId && member.UserId == memberUserId);

        if (membership is null)
        {
            return ServiceResult.Fail("Uye bulunamadi.", ServiceErrorKind.NotFound);
        }

        membership.CanPublishAnnouncements = canPublish;
        _unitOfWork.TeamMembers.Update(membership);
        await _unitOfWork.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<int> PublishDueScheduledAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var dueItems = (await _unitOfWork.TeamAnnouncements.GetAllAsync())
            .Where(item =>
                item.Status == AnnouncementStatus.Scheduled
                && item.ScheduledPublishAt.HasValue
                && item.ScheduledPublishAt.Value <= now)
            .OrderBy(item => item.ScheduledPublishAt)
            .ToList();

        if (dueItems.Count == 0)
        {
            return 0;
        }

        var publishedCount = 0;
        foreach (var announcement in dueItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            announcement.Status = AnnouncementStatus.Published;
            announcement.PublishedAt = DateTime.UtcNow;
            announcement.ScheduledPublishAt = null;
            _unitOfWork.TeamAnnouncements.Update(announcement);
            await _unitOfWork.SaveChangesAsync();

            // Yayinciya bildirim gitmez; diger takim uyelerine gider.
            await NotifyPublishedAsync(
                announcement.TeamId,
                announcement.AuthorUserId,
                announcement,
                includeActor: false);
            publishedCount++;
        }

        return publishedCount;
    }

    public async Task<DateTime?> GetNextScheduledPublishAtUtcAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var next = (await _unitOfWork.TeamAnnouncements.GetAllAsync())
            .Where(item =>
                item.Status == AnnouncementStatus.Scheduled
                && item.ScheduledPublishAt.HasValue)
            .Select(item => item.ScheduledPublishAt!.Value)
            .OrderBy(at => at)
            .Cast<DateTime?>()
            .FirstOrDefault();

        return next;
    }

    private async Task NotifyPublishedAsync(
        int teamId,
        int actorUserId,
        TeamAnnouncement announcement,
        bool includeActor)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        var memberIds = (await _unitOfWork.TeamMembers.GetAllAsync())
            .Where(member => member.TeamId == teamId)
            .Select(member => member.UserId)
            .ToHashSet();

        if (team is not null)
        {
            memberIds.Add(team.LeaderUserId);
        }

        var actor = await _unitOfWork.Users.GetByIdAsync(actorUserId);
        var teamName = team?.Name ?? string.Empty;
        var actorDisplayName = actor is null ? string.Empty : UserDisplayNameHelper.Format(actor);

        await _notificationDispatch.NotifyAnnouncementAsync(
            memberIds,
            actorUserId,
            teamId,
            announcement.Id,
            announcement.Title,
            announcement.Body,
            teamName,
            actorDisplayName,
            includeActor);

        await _boardNotifier.NotifyBoardChangedAsync(
            teamId,
            TeamBoardChangeTypes.AnnouncementPublished,
            actorUserId,
            announcementId: announcement.Id);
    }

    private async Task<bool> CanPublishAsync(Team team, int userId)
    {
        if (team.LeaderUserId == userId)
        {
            return true;
        }

        if (!await _teamService.IsTeamMemberAsync(team.Id, userId))
        {
            return false;
        }

        var membership = (await _unitOfWork.TeamMembers.GetAllAsync())
            .FirstOrDefault(member => member.TeamId == team.Id && member.UserId == userId);

        return membership?.CanPublishAnnouncements == true;
    }

    private static ServiceResult ValidateContent(string title, string body)
    {
        var trimmedTitle = title.Trim();
        var trimmedBody = body.Trim();

        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            return ServiceResult.Fail("Duyuru adi zorunludur.");
        }

        if (trimmedTitle.Length > 200)
        {
            return ServiceResult.Fail("Duyuru adi en fazla 200 karakter olabilir.");
        }

        if (string.IsNullOrWhiteSpace(trimmedBody))
        {
            return ServiceResult.Fail("Duyuru metni zorunludur.");
        }

        if (trimmedBody.Length > 4000)
        {
            return ServiceResult.Fail("Duyuru metni en fazla 4000 karakter olabilir.");
        }

        return ServiceResult.Ok();
    }

    private static string? NormalizePublishMode(string? publishMode)
    {
        var mode = (publishMode ?? string.Empty).Trim();
        if (mode.Equals("Draft", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("Taslak", StringComparison.OrdinalIgnoreCase))
        {
            return "Draft";
        }

        if (mode.Equals("Now", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("Publish", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("Yayinla", StringComparison.OrdinalIgnoreCase))
        {
            return "Now";
        }

        if (mode.Equals("Schedule", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("Zamanla", StringComparison.OrdinalIgnoreCase))
        {
            return "Schedule";
        }

        return null;
    }

    private static DateTime ToUtc(DateTime value)
    {
        // Frontend toISOString() ile UTC gonderir. JSON bazen Kind=Unspecified getirse de
        // deger UTC duvar saatidir; Local sanip tekrar cevirmek saati bozar.
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static TeamAnnouncementDto MapToDto(TeamAnnouncement announcement, User? author)
    {
        return new TeamAnnouncementDto
        {
            Id = announcement.Id,
            TeamId = announcement.TeamId,
            Title = announcement.Title,
            Body = announcement.Body,
            Status = announcement.Status,
            ScheduledPublishAt = announcement.ScheduledPublishAt,
            PublishedAt = announcement.PublishedAt,
            AuthorUserId = announcement.AuthorUserId,
            AuthorDisplayName = author is null ? string.Empty : UserDisplayNameHelper.Format(author),
            AuthorEmail = author?.Email ?? string.Empty,
            CreatedDate = announcement.CreatedDate
        };
    }
}
