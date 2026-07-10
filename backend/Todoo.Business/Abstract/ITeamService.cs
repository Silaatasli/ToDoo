using Todoo.Business.Models;
using Todoo.Business.Models.Teams;

namespace Todoo.Business.Abstract;

public interface ITeamService
{
    Task<ServiceResult<TeamDetailDto>> CreateTeamAsync(string name, IReadOnlyList<string>? columnTitles, int userId);
    Task<IEnumerable<TeamListDto>> GetTeamsForUserAsync(int userId);
    Task<ServiceResult<TeamDetailDto>> GetTeamByIdAsync(int teamId, int userId);
    Task<ServiceResult<TeamBoardDto>> GetTeamBoardAsync(int teamId, int userId);
    Task<ServiceResult<TeamBoardColumnDto>> AddBoardColumnAsync(int teamId, string title, int userId);
    Task<ServiceResult<TeamBoardColumnDto>> UpdateBoardColumnAsync(int teamId, int columnId, string title, int userId);
    Task<ServiceResult> ReorderBoardColumnsAsync(int teamId, IReadOnlyList<int> orderedColumnIds, int userId);
    Task<ServiceResult> DeleteTeamAsync(int teamId, int userId);
    Task<ServiceResult> AddMemberAsync(int teamId, string email, int userId);
    Task<ServiceResult> RemoveMemberAsync(int teamId, int memberUserId, int userId);
    Task<ServiceResult<IEnumerable<TaskActivityLogDto>>> GetTeamActivityAsync(int teamId, int userId);
    Task<bool> IsTeamMemberAsync(int teamId, int userId);
    Task<int> EnsurePersonalTeamAsync(int userId);
}
