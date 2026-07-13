using System.Globalization;
using Todoo.Business.Abstract;
using Todoo.Business.Helpers;
using Todoo.Business.Models;
using Todoo.DataAccess.UnitOfWork;

namespace Todoo.Business.Concrete;

public class SearchService : ISearchService
{
    private const int MaxResultsPerSection = 8;

    private readonly IUnitOfWork _unitOfWork;

    public SearchService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ServiceResult<GlobalSearchResultDto>> SearchAsync(string query, int userId)
    {
        var term = query?.Trim() ?? string.Empty;
        if (term.Length < 3)
        {
            return ServiceResult<GlobalSearchResultDto>.Fail("En az 3 karakter girin.");
        }

        var memberships = await _unitOfWork.TeamMembers.GetAllAsync();
        var myTeamIds = memberships
            .Where(member => member.UserId == userId)
            .Select(member => member.TeamId)
            .ToHashSet();

        var teams = (await _unitOfWork.Teams.GetAllAsync()).ToList();
        var visibleTeams = teams
            .Where(team => myTeamIds.Contains(team.Id) && !team.IsPersonal)
            .ToList();

        var visibleTeamIds = visibleTeams.Select(team => team.Id).ToHashSet();
        var teamNameById = visibleTeams.ToDictionary(team => team.Id, team => team.Name);

        var matchedTeams = visibleTeams
            .Where(team => team.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(team => team.Name, StringComparer.Create(new CultureInfo("tr-TR"), ignoreCase: true))
            .Take(MaxResultsPerSection)
            .Select(team => new GlobalSearchTeamDto
            {
                Id = team.Id,
                Name = team.Name
            })
            .ToList();

        var columns = (await _unitOfWork.TeamBoardColumns.GetAllAsync())
            .Where(column => visibleTeamIds.Contains(column.TeamId))
            .ToDictionary(column => column.Id, column => column.Title);

        var matchedTasks = (await _unitOfWork.TaskItems.GetAllAsync())
            .Where(task => visibleTeamIds.Contains(task.TeamId))
            .Where(task =>
                task.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || (!string.IsNullOrWhiteSpace(task.Description)
                    && task.Description.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
            .OrderByDescending(task => task.CreatedDate)
            .Take(MaxResultsPerSection)
            .Select(task => new GlobalSearchTaskDto
            {
                Id = task.Id,
                Title = task.Title,
                TeamId = task.TeamId,
                TeamName = teamNameById.GetValueOrDefault(task.TeamId, string.Empty),
                BoardColumnTitle = columns.GetValueOrDefault(task.BoardColumnId, string.Empty)
            })
            .ToList();

        var visibleUserIds = memberships
            .Where(member => visibleTeamIds.Contains(member.TeamId))
            .Select(member => member.UserId)
            .ToHashSet();

        var matchedPeople = (await _unitOfWork.Users.GetAllAsync())
            .Where(user => visibleUserIds.Contains(user.Id))
            .Where(user => MatchesPerson(user, term))
            .OrderBy(user => UserDisplayNameHelper.Format(user), StringComparer.Create(new CultureInfo("tr-TR"), ignoreCase: true))
            .Take(MaxResultsPerSection)
            .Select(user => new GlobalSearchPersonDto
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = UserDisplayNameHelper.Format(user),
                HasProfilePhoto = !string.IsNullOrWhiteSpace(user.ProfilePhotoObjectKey)
            })
            .ToList();

        return ServiceResult<GlobalSearchResultDto>.Ok(new GlobalSearchResultDto
        {
            Teams = matchedTeams,
            Tasks = matchedTasks,
            People = matchedPeople
        });
    }

    private static bool MatchesPerson(Entities.Entities.User user, string term)
    {
        var comparison = StringComparison.CurrentCultureIgnoreCase;
        var firstName = user.FirstName ?? string.Empty;
        var lastName = user.LastName ?? string.Empty;
        var fullName = $"{firstName} {lastName}".Trim();
        var displayName = UserDisplayNameHelper.Format(user);

        return firstName.Contains(term, comparison)
            || lastName.Contains(term, comparison)
            || fullName.Contains(term, comparison)
            || displayName.Contains(term, comparison)
            || user.Email.Contains(term, comparison);
    }
}
