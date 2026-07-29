using Todoo.Business.Models;
using Todoo.Business.Models.Sprints;

namespace Todoo.Business.Abstract;

public interface ISprintService
{
    Task<ServiceResult<BoardKapsamDto>> GetKapsamAsync(int teamId, int boardId, int userId);
    Task<ServiceResult<SprintDetailDto>> GetByIdAsync(int sprintId, int userId);
    Task<ServiceResult<SprintDetailDto>> CreateAsync(int teamId, int boardId, CreateSprintRequest request, int userId);
    Task<ServiceResult<SprintDetailDto>> UpdateAsync(int sprintId, UpdateSprintRequest request, int userId);
    Task<ServiceResult> DeleteAsync(int sprintId, int userId);
    Task<ServiceResult<SprintTaskDto>> MoveTaskToSprintAsync(int sprintId, int taskId, MoveTaskToSprintRequest request, int userId);
    Task<ServiceResult> MoveTaskToBacklogAsync(int taskId, int? targetIndex, int userId);
    Task<ServiceResult> ReorderSprintTasksAsync(int sprintId, ReorderSprintTasksRequest request, int userId);
    Task<ServiceResult> ReorderBacklogAsync(int boardId, ReorderSprintTasksRequest request, int userId);
    Task<ServiceResult<SprintDetailDto>> StartAsync(int sprintId, int userId);
    Task<ServiceResult<SprintDetailDto>> CompleteAsync(int sprintId, CompleteSprintRequest request, int userId);
    Task<ServiceResult<SprintDetailDto>> CancelAsync(int sprintId, CancelSprintRequest request, int userId);
}
