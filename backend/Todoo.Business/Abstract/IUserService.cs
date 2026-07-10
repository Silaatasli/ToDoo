using Todoo.Business.Models;

namespace Todoo.Business.Abstract;

public interface IUserService
{
    Task<ServiceResult<UserProfileDto>> GetOwnProfileAsync(int userId);
    Task<ServiceResult<UserProfileDto>> GetProfileAsync(int targetUserId, int requesterUserId);
    Task<ServiceResult<UserProfileDto>> UpdateProfileAsync(int userId, string? firstName, string? lastName, string? phoneNumber, string? title);
    Task<ServiceResult<IEnumerable<UserSearchResultDto>>> SearchUsersAsync(string query, int requesterUserId);
}
