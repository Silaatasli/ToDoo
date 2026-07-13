using Todoo.Business.Models;
using Todoo.Business.Models.Teams;
using Todoo.Entities.Entities;

namespace Todoo.Business.Abstract;

public interface ITaskService
{
    Task<ServiceResult<TaskDetailDto>> GetTaskDetailAsync(int taskId, int userId);
    Task<ServiceResult<IEnumerable<TaskActivityLogDto>>> GetTaskActivityAsync(int taskId, int userId);
    Task<ServiceResult<TaskListDto>> CreateTeamTaskAsync(TaskItem task, int teamId, int boardId, int? boardColumnId, int? assignedToUserId, int userId);
    Task<ServiceResult<TaskListDto>> UpdateTaskAsync(TaskItem task, int userId);
    Task<ServiceResult<TaskListDto>> MoveTaskToColumnAsync(int taskId, int boardColumnId, int userId);
    Task<ServiceResult<TaskListDto>> CompleteTaskAsync(int taskId, int userId);
    Task<ServiceResult<TaskListDto>> ReopenTaskAsync(int taskId, int userId);
    Task<ServiceResult<TaskListDto>> AssignTaskAsync(int taskId, int? assignedToUserId, int userId);
    Task<ServiceResult<TaskListDto>> AcceptAssignmentAsync(int taskId, int userId);
    Task<ServiceResult<TaskListDto>> DeclineAssignmentAsync(int taskId, int userId);
    Task<ServiceResult> DeleteTaskAsync(int taskId, int userId);
}
