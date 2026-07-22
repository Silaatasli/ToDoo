using Todoo.Business.Models;
using Todoo.Business.Models.Teams;

namespace Todoo.Business.Abstract;

public interface ITeamService
{
    Task<ServiceResult<TeamDetailDto>> CreateTeamAsync(string name, string? boardName, IReadOnlyList<string>? columnTitles, int userId);
    Task<IEnumerable<TeamListDto>> GetTeamsForUserAsync(int userId);
    Task<ServiceResult<TeamDetailDto>> GetTeamByIdAsync(int teamId, int userId);
    Task<ServiceResult<IEnumerable<BoardListDto>>> GetBoardsAsync(int teamId, int userId);
    Task<ServiceResult<BoardListDto>> CreateBoardAsync(int teamId, string name, IReadOnlyList<string>? columnTitles, int userId);
    Task<ServiceResult> DeleteBoardAsync(int teamId, int boardId, int userId);
    Task<ServiceResult<TeamBoardDto>> GetBoardAsync(int teamId, int boardId, int userId);
    Task<ServiceResult<TeamBoardDto>> GetTeamBoardAsync(int teamId, int userId);
    Task<ServiceResult<TeamBoardColumnDto>> AddBoardColumnAsync(int teamId, int boardId, string title, int userId);
    Task<ServiceResult<TeamBoardColumnDto>> UpdateBoardColumnAsync(int teamId, int boardId, int columnId, string title, int userId);
    Task<ServiceResult> ReorderBoardColumnsAsync(int teamId, int boardId, IReadOnlyList<int> orderedColumnIds, int userId);
    Task<ServiceResult> DeleteTeamAsync(int teamId, int userId);
    Task<ServiceResult> AddMemberAsync(int teamId, string email, int userId);
    Task<ServiceResult> RemoveMemberAsync(int teamId, int memberUserId, int userId);
    Task<ServiceResult<IEnumerable<TaskActivityLogDto>>> GetTeamActivityAsync(int teamId, int userId);
    Task<bool> IsTeamMemberAsync(int teamId, int userId);
    Task<IReadOnlyList<int>> GetTeamIdsForUserAsync(int userId);
    Task<int> EnsurePersonalTeamAsync(int userId);
}
