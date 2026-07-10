using System.Globalization;
using Todoo.Business.Abstract;
using Todoo.Business.Helpers;
using Todoo.Business.Models;
using Todoo.DataAccess.UnitOfWork;
using Todoo.Entities.Entities;

namespace Todoo.Business.Concrete;

public class UserService : IUserService
{
    private readonly IUnitOfWork _unitOfWork;

    public UserService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ServiceResult<UserProfileDto>> GetOwnProfileAsync(int userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user is null)
        {
            return ServiceResult<UserProfileDto>.Fail("Kullanici bulunamadi.", ServiceErrorKind.NotFound);
        }

        return ServiceResult<UserProfileDto>.Ok(MapToDto(user, isSelf: true));
    }

    public async Task<ServiceResult<UserProfileDto>> GetProfileAsync(int targetUserId, int requesterUserId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(targetUserId);
        if (user is null)
        {
            return ServiceResult<UserProfileDto>.Fail("Kullanici bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (targetUserId != requesterUserId && !await SharesTeamAsync(targetUserId, requesterUserId))
        {
            return ServiceResult<UserProfileDto>.Fail("Bu kullanicinin profilini goruntuleme yetkiniz yok.", ServiceErrorKind.Forbidden);
        }

        return ServiceResult<UserProfileDto>.Ok(MapToDto(user, isSelf: targetUserId == requesterUserId));
    }

    public async Task<ServiceResult<UserProfileDto>> UpdateProfileAsync(int userId, string? firstName, string? lastName, string? phoneNumber, string? title)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user is null)
        {
            return ServiceResult<UserProfileDto>.Fail("Kullanici bulunamadi.", ServiceErrorKind.NotFound);
        }

        user.FirstName = Normalize(firstName);
        user.LastName = Normalize(lastName);
        user.PhoneNumber = Normalize(phoneNumber);
        user.Title = Normalize(title);

        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync();

        return ServiceResult<UserProfileDto>.Ok(MapToDto(user, isSelf: true));
    }

    public async Task<ServiceResult<IEnumerable<UserSearchResultDto>>> SearchUsersAsync(string query, int requesterUserId)
    {
        var term = query?.Trim();
        if (string.IsNullOrWhiteSpace(term) || term.Length < 3)
        {
            return ServiceResult<IEnumerable<UserSearchResultDto>>.Fail("En az 3 karakter girin.");
        }

        var users = (await _unitOfWork.Users.GetAllAsync())
            .Where(user => MatchesSearch(user, term))
            .OrderBy(user => UserDisplayNameHelper.Format(user), StringComparer.Create(new CultureInfo("tr-TR"), ignoreCase: true))
            .Take(15)
            .Select(user => new UserSearchResultDto
            {
                Id = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                DisplayName = UserDisplayNameHelper.Format(user)
            })
            .ToList();

        return ServiceResult<IEnumerable<UserSearchResultDto>>.Ok(users);
    }

    private async Task<bool> SharesTeamAsync(int targetUserId, int requesterUserId)
    {
        var memberships = await _unitOfWork.TeamMembers.GetAllAsync();
        var requesterTeams = memberships
            .Where(member => member.UserId == requesterUserId)
            .Select(member => member.TeamId)
            .ToHashSet();

        return memberships.Any(member => member.UserId == targetUserId && requesterTeams.Contains(member.TeamId));
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static bool MatchesSearch(User user, string term)
    {
        var comparison = StringComparison.CurrentCultureIgnoreCase;
        var firstName = user.FirstName ?? string.Empty;
        var lastName = user.LastName ?? string.Empty;
        var fullName = $"{firstName} {lastName}".Trim();

        return firstName.Contains(term, comparison)
            || lastName.Contains(term, comparison)
            || fullName.Contains(term, comparison);
    }

    private static UserProfileDto MapToDto(User user, bool isSelf) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        PhoneNumber = user.PhoneNumber,
        Title = user.Title,
        CreatedDate = user.CreatedDate,
        IsSelf = isSelf
    };
}
