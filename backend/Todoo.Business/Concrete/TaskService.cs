using Todoo.Business.Abstract;
using Todoo.Business.Helpers;
using Todoo.Business.Models;
using Todoo.Business.Models.Teams;
using Todoo.DataAccess.UnitOfWork;
using Todoo.Entities.Entities;
using Todoo.Entities.Enums;

namespace Todoo.Business.Concrete;

public class TaskService : ITaskService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICategoryService _categoryService;
    private readonly ITeamService _teamService;
    private readonly ITeamBoardNotifier _boardNotifier;
    private readonly ILuceneSearchIndex _searchIndex;
    private readonly NotificationDispatchService _notificationDispatch;

    public TaskService(
        IUnitOfWork unitOfWork,
        ICategoryService categoryService,
        ITeamService teamService,
        ITeamBoardNotifier boardNotifier,
        ILuceneSearchIndex searchIndex,
        NotificationDispatchService notificationDispatch)
    {
        _unitOfWork = unitOfWork;
        _categoryService = categoryService;
        _teamService = teamService;
        _boardNotifier = boardNotifier;
        _searchIndex = searchIndex;
        _notificationDispatch = notificationDispatch;
    }

    public async Task<ServiceResult<TaskDetailDto>> GetTaskDetailAsync(int taskId, int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<TaskDetailDto>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        return ServiceResult<TaskDetailDto>.Ok(await MapToDetailDtoAsync(taskResult.Data!));
    }

    public async Task<ServiceResult<IEnumerable<TaskActivityLogDto>>> GetTaskActivityAsync(int taskId, int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<IEnumerable<TaskActivityLogDto>>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var users = (await _unitOfWork.Users.GetAllAsync()).ToList();
        var displayNameByEmail = UserDisplayNameHelper.BuildDisplayNameByEmail(users);
        var userEmails = users.ToDictionary(user => user.Id, user => user.Email);
        var logs = (await _unitOfWork.TaskActivityLogs.GetAllAsync())
            .Where(log => log.TaskId == taskId)
            .OrderByDescending(log => log.CreatedDate)
            .Select(log =>
            {
                var dto = new TaskActivityLogDto
                {
                    Id = log.Id,
                    TaskId = log.TaskId,
                    UserId = log.UserId,
                    UserEmail = userEmails.GetValueOrDefault(log.UserId, string.Empty),
                    ActionType = log.ActionType,
                    OldValue = log.OldValue,
                    NewValue = log.NewValue,
                    CreatedDate = log.CreatedDate
                };
                UserDisplayNameHelper.ApplyAssigneeDisplayNames(dto, displayNameByEmail);
                return dto;
            });

        return ServiceResult<IEnumerable<TaskActivityLogDto>>.Ok(logs);
    }

    public async Task<ServiceResult<TaskListDto>> CreateTeamTaskAsync(
        TaskItem task,
        int teamId,
        int boardId,
        int? boardColumnId,
        int? assignedToUserId,
        int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult<TaskListDto>.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await _teamService.IsTeamMemberAsync(teamId, userId))
        {
            return ServiceResult<TaskListDto>.Fail("Bu takimin uyesi degilsiniz.", ServiceErrorKind.Forbidden);
        }

        var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
        if (board is null || board.TeamId != teamId)
        {
            return ServiceResult<TaskListDto>.Fail("Pano bulunamadi.", ServiceErrorKind.NotFound);
        }

        var categoryResult = await ResolveCategoryIdAsync(task.CategoryId);
        if (!categoryResult.Success)
        {
            return ServiceResult<TaskListDto>.Fail(categoryResult.ErrorMessage!);
        }

        var columns = (await _unitOfWork.TeamBoardColumns.GetAllAsync())
            .Where(column => column.BoardId == boardId)
            .OrderBy(column => column.DisplayOrder)
            .ToList();

        if (columns.Count == 0)
        {
            return ServiceResult<TaskListDto>.Fail("Panoda sutun bulunamadi.");
        }

        TeamBoardColumn? targetColumn;
        if (boardColumnId.HasValue)
        {
            targetColumn = columns.FirstOrDefault(column => column.Id == boardColumnId.Value);
            if (targetColumn is null)
            {
                return ServiceResult<TaskListDto>.Fail(
                    $"Gecersiz boardColumnId: {boardColumnId}. GET /api/teams/{teamId}/boards/{boardId} ile bu panoya ait sutun id'lerini kontrol et");
            }
        }
        else
        {
            targetColumn = columns.First();
        }

        if (assignedToUserId.HasValue && !await _teamService.IsTeamMemberAsync(teamId, assignedToUserId.Value))
        {
            return ServiceResult<TaskListDto>.Fail("Atanan kullanici bu takimin uyesi degil.");
        }

        task.TeamId = teamId;
        task.BoardId = boardId;
        task.BoardColumnId = targetColumn.Id;
        task.DisplayOrder = await GetNextDisplayOrderAsync(targetColumn.Id);
        task.CreatedByUserId = userId;
        task.AssignedToUserId = assignedToUserId;
        task.CategoryId = categoryResult.Data;
        task.IsCompleted = targetColumn.IsCompletedColumn;
        ApplyAssignmentState(task, assignedToUserId, userId);

        _unitOfWork.TaskItems.Add(task);
        await _unitOfWork.SaveChangesAsync();

        await LogActivityAsync(task.TeamId, task.Id, userId, TaskActivityAction.TaskCreated, null, task.Title);

        if (assignedToUserId.HasValue)
        {
            var assignee = await _unitOfWork.Users.GetByIdAsync(assignedToUserId.Value);
            var assigneeName = assignee is null ? null : UserDisplayNameHelper.Format(assignee);
            await LogActivityAsync(task.TeamId, task.Id, userId, TaskActivityAction.Assigned, null, assigneeName);
            await _notificationDispatch.NotifyTaskAssignedAsync(
                assignedToUserId.Value,
                userId,
                task.TeamId,
                task.BoardId,
                task.Id,
                task.Title);
        }

        await _boardNotifier.NotifyBoardChangedAsync(teamId, TeamBoardChangeTypes.TaskCreated, userId, task.Id, boardId);
        await IndexTaskDocumentAsync(task);

        return ServiceResult<TaskListDto>.Ok(await MapToListDtoAsync(task));
    }

    public async Task<ServiceResult<TaskListDto>> CreateSubtaskAsync(
        int parentTaskId,
        string title,
        string? description,
        int? assignedToUserId,
        int userId)
    {
        var parentResult = await GetTaskIfMemberAsync(parentTaskId, userId);
        if (!parentResult.Success)
        {
            return ServiceResult<TaskListDto>.Fail(
                parentResult.ErrorMessage!,
                parentResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var parent = parentResult.Data!;
        if (parent.ParentTaskId.HasValue)
        {
            return ServiceResult<TaskListDto>.Fail("Alt goreve yeni alt gorev eklenemez.");
        }

        var trimmed = title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return ServiceResult<TaskListDto>.Fail("Alt gorev basligi zorunludur.");
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is { Length: > 4000 })
        {
            return ServiceResult<TaskListDto>.Fail("Aciklama en fazla 4000 karakter olabilir.");
        }

        if (assignedToUserId.HasValue && !await _teamService.IsTeamMemberAsync(parent.TeamId, assignedToUserId.Value))
        {
            return ServiceResult<TaskListDto>.Fail("Atanan kullanici bu takimin uyesi degil.");
        }

        var subtask = new TaskItem
        {
            TeamId = parent.TeamId,
            BoardId = parent.BoardId,
            BoardColumnId = parent.BoardColumnId,
            ParentTaskId = parent.Id,
            SubtaskStatus = SubtaskStatus.Todo,
            DisplayOrder = await GetNextSubtaskDisplayOrderAsync(parent.Id),
            CreatedByUserId = userId,
            Title = trimmed,
            Description = trimmedDescription,
            Priority = parent.Priority,
            StartDate = DateTime.UtcNow,
            IsCompleted = false,
            AssignedToUserId = assignedToUserId
        };

        ApplyAssignmentState(subtask, assignedToUserId, userId);

        _unitOfWork.TaskItems.Add(subtask);
        await _unitOfWork.SaveChangesAsync();

        await LogActivityAsync(subtask.TeamId, parent.Id, userId, TaskActivityAction.Updated, null, $"Alt gorev: {subtask.Title}");
        await SyncParentCompletionAsync(parent.Id, userId);
        await _boardNotifier.NotifyBoardChangedAsync(
            parent.TeamId,
            TeamBoardChangeTypes.TaskUpdated,
            userId,
            parent.Id,
            parent.BoardId);

        if (assignedToUserId.HasValue)
        {
            await _notificationDispatch.NotifyTaskAssignedAsync(
                assignedToUserId.Value,
                userId,
                subtask.TeamId,
                subtask.BoardId,
                subtask.Id,
                subtask.Title);
        }

        return ServiceResult<TaskListDto>.Ok(await MapToListDtoAsync(subtask));
    }

    public async Task<ServiceResult<TaskListDto>> UpdateSubtaskStatusAsync(int taskId, SubtaskStatus status, int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<TaskListDto>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var task = taskResult.Data!;
        if (!task.ParentTaskId.HasValue)
        {
            return ServiceResult<TaskListDto>.Fail("Bu endpoint yalnizca alt gorevler icindir.");
        }

        task.SubtaskStatus = status;
        task.IsCompleted = status == SubtaskStatus.Done;
        if (status == SubtaskStatus.Done)
        {
            task.CompletedAt ??= DateTime.UtcNow;
        }
        else
        {
            task.CompletedAt = null;
        }

        _unitOfWork.TaskItems.Update(task);
        await _unitOfWork.SaveChangesAsync();

        await LogActivityAsync(
            task.TeamId,
            task.Id,
            userId,
            TaskActivityAction.Updated,
            null,
            SubtaskStatusLabel(status));

        await SyncParentCompletionAsync(task.ParentTaskId.Value, userId);
        await _boardNotifier.NotifyBoardChangedAsync(
            task.TeamId,
            TeamBoardChangeTypes.TaskUpdated,
            userId,
            task.ParentTaskId.Value,
            task.BoardId);

        return ServiceResult<TaskListDto>.Ok(await MapToListDtoAsync(task));
    }

    public async Task<ServiceResult<TaskListDto>> UpdateTaskAsync(TaskItem task, int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(task.Id, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<TaskListDto>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var existingTask = taskResult.Data!;
        var categoryResult = await ResolveCategoryIdAsync(task.CategoryId);
        if (!categoryResult.Success)
        {
            return ServiceResult<TaskListDto>.Fail(categoryResult.ErrorMessage!);
        }

        existingTask.Title = task.Title;
        existingTask.Description = task.Description;
        existingTask.CategoryId = categoryResult.Data;
        existingTask.Priority = task.Priority;
        existingTask.StartDate = task.StartDate;
        existingTask.DueDate = task.DueDate;

        _unitOfWork.TaskItems.Update(existingTask);
        await _unitOfWork.SaveChangesAsync();

        await LogActivityAsync(existingTask.TeamId, existingTask.Id, userId, TaskActivityAction.Updated, null, existingTask.Title);
        await _boardNotifier.NotifyBoardChangedAsync(existingTask.TeamId, TeamBoardChangeTypes.TaskUpdated, userId, existingTask.Id, existingTask.BoardId);
        await IndexTaskDocumentAsync(existingTask);

        return ServiceResult<TaskListDto>.Ok(await MapToListDtoAsync(existingTask));
    }

    public async Task<ServiceResult<TaskListDto>> MoveTaskToColumnAsync(
        int taskId,
        int boardColumnId,
        int userId,
        int? targetIndex = null,
        bool completeRemainingSubtasks = false)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<TaskListDto>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var task = taskResult.Data!;
        if (task.ParentTaskId.HasValue)
        {
            return ServiceResult<TaskListDto>.Fail("Alt gorevler panoda tasinamaz; durumunu degistirin.");
        }

        var newColumn = await _unitOfWork.TeamBoardColumns.GetByIdAsync(boardColumnId);
        if (newColumn is null)
        {
            return ServiceResult<TaskListDto>.Fail("Pano sutunu bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (newColumn.BoardId != task.BoardId)
        {
            return ServiceResult<TaskListDto>.Fail(
                $"boardColumnId {boardColumnId} bu gorevin panosuna (boardId: {task.BoardId}) ait degil.");
        }

        if (newColumn.IsCompletedColumn)
        {
            var ready = await PrepareParentCompletionAsync(task.Id, userId, completeRemainingSubtasks);
            if (!ready.Success)
            {
                return ServiceResult<TaskListDto>.Fail(ready.ErrorMessage!);
            }
        }

        var wasCompleted = task.IsCompleted;
        var moved = await ApplyColumnChangeAsync(task, newColumn, userId, targetIndex);
        if (!moved.Success || newColumn.IsCompletedColumn)
        {
            return moved;
        }

        if (wasCompleted)
        {
            // Tamamlandidan cikinca alt gorev progressi sifirlanir.
            await ResetSubtasksProgressAsync(task.Id, userId);
        }
        else
        {
            // Tum alt gorevler bitmisse parent yeniden tamamlanir.
            await SyncParentCompletionAsync(task.Id, userId);
        }

        return ServiceResult<TaskListDto>.Ok(await MapToListDtoAsync(
            (await _unitOfWork.TaskItems.GetByIdAsync(task.Id))!));
    }

    public async Task<ServiceResult<TaskListDto>> CompleteTaskAsync(
        int taskId,
        int userId,
        bool completeRemainingSubtasks = false)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<TaskListDto>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var task = taskResult.Data!;
        if (task.ParentTaskId.HasValue)
        {
            return await UpdateSubtaskStatusAsync(taskId, SubtaskStatus.Done, userId);
        }

        var ready = await PrepareParentCompletionAsync(task.Id, userId, completeRemainingSubtasks);
        if (!ready.Success)
        {
            return ServiceResult<TaskListDto>.Fail(ready.ErrorMessage!);
        }

        var columns = await GetBoardColumnsAsync(task.BoardId);
        var completedColumn = columns.FirstOrDefault(column => column.IsCompletedColumn);
        if (completedColumn is null)
        {
            return ServiceResult<TaskListDto>.Fail("Bu gorevin panosunda tamamlandi sutunu bulunamadi.");
        }

        return await ApplyColumnChangeAsync(task, completedColumn, userId);
    }

    public async Task<ServiceResult<TaskListDto>> ReopenTaskAsync(int taskId, int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<TaskListDto>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var task = taskResult.Data!;
        if (task.ParentTaskId.HasValue)
        {
            return await UpdateSubtaskStatusAsync(taskId, SubtaskStatus.Todo, userId);
        }

        var columns = await GetBoardColumnsAsync(task.BoardId);
        var activeColumn = columns.FirstOrDefault(column => !column.IsCompletedColumn);
        if (activeColumn is null)
        {
            return ServiceResult<TaskListDto>.Fail("Bu gorevin panosunda aktif sutun bulunamadi.");
        }

        var reopened = await ApplyColumnChangeAsync(task, activeColumn, userId);
        if (!reopened.Success)
        {
            return reopened;
        }

        await ResetSubtasksProgressAsync(task.Id, userId);
        return ServiceResult<TaskListDto>.Ok(await MapToListDtoAsync(
            (await _unitOfWork.TaskItems.GetByIdAsync(task.Id))!));
    }

    public async Task<ServiceResult<TaskListDto>> AssignTaskAsync(int taskId, int? assignedToUserId, int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<TaskListDto>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var task = taskResult.Data!;
        if (assignedToUserId.HasValue && !await _teamService.IsTeamMemberAsync(task.TeamId, assignedToUserId.Value))
        {
            return ServiceResult<TaskListDto>.Fail("Atanan kullanici bu takimin uyesi degil.");
        }

        var oldAssignee = task.AssignedToUserId.HasValue
            ? await _unitOfWork.Users.GetByIdAsync(task.AssignedToUserId.Value)
            : null;
        var oldAssigneeName = oldAssignee is null ? null : UserDisplayNameHelper.Format(oldAssignee);

        task.AssignedToUserId = assignedToUserId;
        ApplyAssignmentState(task, assignedToUserId, userId);
        _unitOfWork.TaskItems.Update(task);
        await _unitOfWork.SaveChangesAsync();

        var newAssignee = assignedToUserId.HasValue
            ? await _unitOfWork.Users.GetByIdAsync(assignedToUserId.Value)
            : null;
        var newAssigneeName = newAssignee is null ? null : UserDisplayNameHelper.Format(newAssignee);

        await LogActivityAsync(task.TeamId, task.Id, userId, TaskActivityAction.Assigned, oldAssigneeName, newAssigneeName);
        await _boardNotifier.NotifyBoardChangedAsync(task.TeamId, TeamBoardChangeTypes.TaskAssigned, userId, task.Id, task.BoardId);

        if (assignedToUserId.HasValue)
        {
            await _notificationDispatch.NotifyTaskAssignedAsync(
                assignedToUserId.Value,
                userId,
                task.TeamId,
                task.BoardId,
                task.Id,
                task.Title);
        }

        return ServiceResult<TaskListDto>.Ok(await MapToListDtoAsync(task));
    }

    public async Task<ServiceResult<TaskListDto>> AcceptAssignmentAsync(int taskId, int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<TaskListDto>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var task = taskResult.Data!;
        if (task.AssignedToUserId != userId)
        {
            return ServiceResult<TaskListDto>.Fail("Bu gorevi yalnizca atanan kisi onaylayabilir.", ServiceErrorKind.Forbidden);
        }

        if (task.AssignmentStatus != AssignmentStatus.Pending)
        {
            return ServiceResult<TaskListDto>.Fail("Bu gorev onay bekleyen durumda degil.");
        }

        MarkAssignmentAccepted(task);

        var columns = await GetBoardColumnsAsync(task.BoardId);
        var inProgressColumn = FindInProgressColumn(columns);
        if (inProgressColumn is not null && inProgressColumn.Id != task.BoardColumnId)
        {
            task.BoardColumnId = inProgressColumn.Id;
            task.IsCompleted = inProgressColumn.IsCompletedColumn;
        }

        _unitOfWork.TaskItems.Update(task);
        await _unitOfWork.SaveChangesAsync();

        var assignee = await _unitOfWork.Users.GetByIdAsync(userId);
        var assigneeName = assignee is null ? "Atanan kisi" : UserDisplayNameHelper.Format(assignee);
        await LogActivityAsync(task.TeamId, task.Id, userId, TaskActivityAction.AssignmentAccepted, null, assigneeName);
        await _boardNotifier.NotifyBoardChangedAsync(
            task.TeamId,
            TeamBoardChangeTypes.TaskAssignmentAccepted,
            userId,
            task.Id,
            task.BoardId);
        await IndexTaskDocumentAsync(task);

        return ServiceResult<TaskListDto>.Ok(await MapToListDtoAsync(task));
    }

    public async Task<ServiceResult<TaskListDto>> DeclineAssignmentAsync(int taskId, int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<TaskListDto>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var task = taskResult.Data!;
        if (task.AssignedToUserId != userId)
        {
            return ServiceResult<TaskListDto>.Fail("Bu gorevi yalnizca atanan kisi reddedebilir.", ServiceErrorKind.Forbidden);
        }

        if (task.AssignmentStatus != AssignmentStatus.Pending)
        {
            return ServiceResult<TaskListDto>.Fail("Bu gorev onay bekleyen durumda degil.");
        }

        var assignee = await _unitOfWork.Users.GetByIdAsync(userId);
        var assigneeName = assignee is null ? "Atanan kisi" : UserDisplayNameHelper.Format(assignee);

        task.AssignmentStatus = AssignmentStatus.None;
        task.AssignedToUserId = null;

        _unitOfWork.TaskItems.Update(task);
        await _unitOfWork.SaveChangesAsync();

        await LogActivityAsync(task.TeamId, task.Id, userId, TaskActivityAction.AssignmentDeclined, assigneeName, null);
        await _boardNotifier.NotifyBoardChangedAsync(
            task.TeamId,
            TeamBoardChangeTypes.TaskAssignmentDeclined,
            userId,
            task.Id,
            task.BoardId);

        return ServiceResult<TaskListDto>.Ok(await MapToListDtoAsync(task));
    }

    public async Task<ServiceResult> DeleteTaskAsync(int taskId, int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var task = taskResult.Data!;
        var teamId = task.TeamId;
        var boardId = task.BoardId;
        var parentId = task.ParentTaskId;
        var title = task.Title;
        var now = DateTime.UtcNow;

        task.DeletedAt = now;
        task.DeletedByUserId = userId;
        _unitOfWork.TaskItems.Update(task);

        if (!parentId.HasValue)
        {
            var children = (await _unitOfWork.TaskItems.GetAllAsync())
                .Where(item => item.ParentTaskId == task.Id)
                .ToList();
            foreach (var child in children)
            {
                child.DeletedAt = now;
                child.DeletedByUserId = userId;
                _unitOfWork.TaskItems.Update(child);
                _searchIndex.RemoveTask(child.Id);
            }
        }

        await _unitOfWork.SaveChangesAsync();

        await LogActivityAsync(teamId, taskId, userId, TaskActivityAction.Deleted, title, null);

        if (parentId.HasValue)
        {
            await SyncParentCompletionAsync(parentId.Value, userId);
            await _boardNotifier.NotifyBoardChangedAsync(
                teamId,
                TeamBoardChangeTypes.TaskUpdated,
                userId,
                parentId.Value,
                boardId);
        }
        else
        {
            await _boardNotifier.NotifyBoardChangedAsync(teamId, TeamBoardChangeTypes.TaskDeleted, userId, taskId, boardId);
        }

        _searchIndex.RemoveTask(taskId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<TaskListDto>> RestoreTaskAsync(int taskId, int userId)
    {
        var task = await _unitOfWork.TaskItems.GetByIdIgnoreFiltersAsync(taskId);
        if (task is null || task.DeletedAt is null)
        {
            return ServiceResult<TaskListDto>.Fail("Gorev bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await _teamService.IsTeamMemberAsync(task.TeamId, userId))
        {
            return ServiceResult<TaskListDto>.Fail("Bu gorevi geri yukleme yetkiniz yok.", ServiceErrorKind.Forbidden);
        }

        var board = await _unitOfWork.Boards.GetByIdAsync(task.BoardId);
        if (board is null || board.TeamId != task.TeamId)
        {
            return ServiceResult<TaskListDto>.Fail(
                "Gorevin panosu artik mevcut degil; geri yuklenemez.",
                ServiceErrorKind.Validation);
        }

        var column = await _unitOfWork.TeamBoardColumns.GetByIdAsync(task.BoardColumnId);
        if (column is null || column.BoardId != task.BoardId)
        {
            return ServiceResult<TaskListDto>.Fail(
                "Gorevin sutunu artik mevcut degil; geri yuklenemez.",
                ServiceErrorKind.Validation);
        }

        task.DeletedAt = null;
        task.DeletedByUserId = null;
        task.DisplayOrder = await GetNextDisplayOrderAsync(task.BoardColumnId);
        _unitOfWork.TaskItems.Update(task);
        await _unitOfWork.SaveChangesAsync();

        await LogActivityAsync(task.TeamId, task.Id, userId, TaskActivityAction.TaskCreated, null, task.Title);
        await _boardNotifier.NotifyBoardChangedAsync(
            task.TeamId,
            TeamBoardChangeTypes.TaskCreated,
            userId,
            task.Id,
            task.BoardId);
        await IndexTaskDocumentAsync(task);

        return ServiceResult<TaskListDto>.Ok(await MapToListDtoAsync(task));
    }

    private async Task<ServiceResult<TaskItem>> GetTaskIfMemberAsync(int taskId, int userId)
    {
        var task = await _unitOfWork.TaskItems.GetByIdAsync(taskId);
        if (task is null || task.DeletedAt is not null)
        {
            return ServiceResult<TaskItem>.Fail("Gorev bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await _teamService.IsTeamMemberAsync(task.TeamId, userId))
        {
            return ServiceResult<TaskItem>.Fail("Bu gorevi gorme yetkiniz yok.", ServiceErrorKind.Forbidden);
        }

        return ServiceResult<TaskItem>.Ok(task);
    }

    private async Task<List<TeamBoardColumn>> GetBoardColumnsAsync(int boardId)
    {
        return (await _unitOfWork.TeamBoardColumns.GetAllAsync())
            .Where(column => column.BoardId == boardId)
            .OrderBy(column => column.DisplayOrder)
            .ToList();
    }

    private async Task<ServiceResult<TaskListDto>> ApplyColumnChangeAsync(
        TaskItem task,
        TeamBoardColumn newColumn,
        int userId,
        int? targetIndex = null)
    {
        var oldColumnId = task.BoardColumnId;
        var oldColumn = await _unitOfWork.TeamBoardColumns.GetByIdAsync(oldColumnId);
        var oldTitle = oldColumn?.Title;
        var sameColumn = oldColumnId == newColumn.Id;

        var currentIndex = sameColumn
            ? (await GetOrderedColumnTasksAsync(oldColumnId)).FindIndex(item => item.Id == task.Id)
            : -1;

        var siblings = (await GetOrderedColumnTasksAsync(newColumn.Id))
            .Where(item => item.Id != task.Id)
            .ToList();

        var insertAt = targetIndex ?? siblings.Count;
        if (insertAt < 0)
        {
            insertAt = 0;
        }
        if (insertAt > siblings.Count)
        {
            insertAt = siblings.Count;
        }

        if (sameColumn && currentIndex == insertAt)
        {
            return ServiceResult<TaskListDto>.Ok(await MapToListDtoAsync(task));
        }

        var wasCompleted = task.IsCompleted;
        task.BoardColumnId = newColumn.Id;
        task.IsCompleted = newColumn.IsCompletedColumn;
        if (newColumn.IsCompletedColumn)
        {
            if (!wasCompleted)
            {
                task.CompletedAt = DateTime.UtcNow;
            }
        }
        else if (wasCompleted)
        {
            task.CompletedAt = null;
        }

        task.DisplayOrder = insertAt;
        _unitOfWork.TaskItems.Update(task);

        siblings.Insert(insertAt, task);
        for (var i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].DisplayOrder == i)
            {
                continue;
            }

            siblings[i].DisplayOrder = i;
            _unitOfWork.TaskItems.Update(siblings[i]);
        }

        if (!sameColumn)
        {
            await CompactColumnOrdersAsync(oldColumnId);
        }

        await _unitOfWork.SaveChangesAsync();

        if (!sameColumn)
        {
            await LogActivityAsync(
                task.TeamId,
                task.Id,
                userId,
                TaskActivityAction.ColumnChanged,
                oldTitle,
                newColumn.Title);
        }

        await _boardNotifier.NotifyBoardChangedAsync(task.TeamId, TeamBoardChangeTypes.TaskMoved, userId, task.Id, task.BoardId);
        await IndexTaskDocumentAsync(task);

        return ServiceResult<TaskListDto>.Ok(await MapToListDtoAsync(task));
    }

    private async Task<List<TaskItem>> GetOrderedColumnTasksAsync(int boardColumnId)
    {
        return (await _unitOfWork.TaskItems.GetAllAsync())
            .Where(item => item.BoardColumnId == boardColumnId && item.ParentTaskId == null)
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Id)
            .ToList();
    }

    private async Task CompactColumnOrdersAsync(int boardColumnId)
    {
        var remaining = await GetOrderedColumnTasksAsync(boardColumnId);
        for (var i = 0; i < remaining.Count; i++)
        {
            if (remaining[i].DisplayOrder == i)
            {
                continue;
            }

            remaining[i].DisplayOrder = i;
            _unitOfWork.TaskItems.Update(remaining[i]);
        }
    }

    private async Task<int> GetNextDisplayOrderAsync(int boardColumnId)
    {
        var tasks = await GetOrderedColumnTasksAsync(boardColumnId);
        return tasks.Count == 0 ? 0 : tasks.Max(item => item.DisplayOrder) + 1;
    }

    private async Task SyncParentCompletionAsync(int parentTaskId, int userId)
    {
        var parent = await _unitOfWork.TaskItems.GetByIdAsync(parentTaskId);
        if (parent is null || parent.ParentTaskId.HasValue)
        {
            return;
        }

        var subtasks = await GetSubtasksAsync(parentTaskId);
        if (subtasks.Count == 0)
        {
            return;
        }

        var allDone = subtasks.All(item => item.SubtaskStatus == SubtaskStatus.Done);
        var anyInProgress = subtasks.Any(item => item.SubtaskStatus == SubtaskStatus.InProgress);

        if (allDone && !parent.IsCompleted)
        {
            var columns = await GetBoardColumnsAsync(parent.BoardId);
            var completedColumn = columns.FirstOrDefault(column => column.IsCompletedColumn);
            if (completedColumn is not null)
            {
                await ApplyColumnChangeAsync(parent, completedColumn, userId);
            }

            return;
        }

        if (!allDone && parent.IsCompleted)
        {
            var columns = await GetBoardColumnsAsync(parent.BoardId);
            var inProgressColumn = FindInProgressColumn(columns);
            if (inProgressColumn is not null)
            {
                await ApplyColumnChangeAsync(parent, inProgressColumn, userId);
            }
            return;
        }

        if (anyInProgress && !parent.IsCompleted)
        {
            var columns = await GetBoardColumnsAsync(parent.BoardId);
            var currentColumn = columns.FirstOrDefault(c => c.Id == parent.BoardColumnId);
            var inProgressColumn = FindInProgressColumn(columns);
            if (inProgressColumn is not null && currentColumn?.Id != inProgressColumn.Id)
            {
                await ApplyColumnChangeAsync(parent, inProgressColumn, userId);
            }

            return;
        }

        // Tek alt gorev Yapilacak'a donduyse ana gorevi In Progress'ten Todo sutununa cek.
        if (subtasks.Count == 1
            && subtasks[0].SubtaskStatus == SubtaskStatus.Todo
            && !parent.IsCompleted)
        {
            var columns = await GetBoardColumnsAsync(parent.BoardId);
            var inProgressColumn = FindInProgressColumn(columns);
            var todoColumn = FindTodoColumn(columns);
            if (todoColumn is not null
                && inProgressColumn is not null
                && parent.BoardColumnId == inProgressColumn.Id
                && todoColumn.Id != inProgressColumn.Id)
            {
                await ApplyColumnChangeAsync(parent, todoColumn, userId);
            }
        }
    }

    /// <summary>
    /// Alt gorevler varken ana gorev ancak hepsi Done ise tamamlanabilir.
    /// completeRemainingSubtasks=true ise eksikler once Done yapilir.
    /// </summary>
    private async Task<ServiceResult> PrepareParentCompletionAsync(
        int parentTaskId,
        int userId,
        bool completeRemainingSubtasks)
    {
        var subtasks = await GetSubtasksAsync(parentTaskId);
        if (subtasks.Count == 0)
        {
            return ServiceResult.Ok();
        }

        var incomplete = subtasks.Where(item => item.SubtaskStatus != SubtaskStatus.Done).ToList();
        if (incomplete.Count == 0)
        {
            return ServiceResult.Ok();
        }

        if (!completeRemainingSubtasks)
        {
            var done = subtasks.Count - incomplete.Count;
            return ServiceResult.Fail(
                $"Once tum alt gorevleri tamamlayin ({done}/{subtasks.Count}).");
        }

        foreach (var subtask in incomplete)
        {
            subtask.SubtaskStatus = SubtaskStatus.Done;
            subtask.IsCompleted = true;
            subtask.CompletedAt ??= DateTime.UtcNow;
            _unitOfWork.TaskItems.Update(subtask);
            await LogActivityAsync(
                subtask.TeamId,
                subtask.Id,
                userId,
                TaskActivityAction.Updated,
                null,
                SubtaskStatusLabel(SubtaskStatus.Done));
        }

        await _unitOfWork.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    /// <summary>
    /// Ana gorev yeniden acilinca tum alt gorevleri Todo'ya ceker (progress 0/N).
    /// </summary>
    private async Task ResetSubtasksProgressAsync(int parentTaskId, int userId)
    {
        var subtasks = await GetSubtasksAsync(parentTaskId);
        var toReset = subtasks.Where(item => item.SubtaskStatus != SubtaskStatus.Todo).ToList();
        if (toReset.Count == 0)
        {
            return;
        }

        foreach (var subtask in toReset)
        {
            subtask.SubtaskStatus = SubtaskStatus.Todo;
            subtask.IsCompleted = false;
            subtask.CompletedAt = null;
            _unitOfWork.TaskItems.Update(subtask);
            await LogActivityAsync(
                subtask.TeamId,
                subtask.Id,
                userId,
                TaskActivityAction.Updated,
                null,
                SubtaskStatusLabel(SubtaskStatus.Todo));
        }

        await _unitOfWork.SaveChangesAsync();
    }

    private async Task<List<TaskItem>> GetSubtasksAsync(int parentTaskId)
    {
        return (await _unitOfWork.TaskItems.GetAllAsync())
            .Where(item => item.ParentTaskId == parentTaskId)
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Id)
            .ToList();
    }

    private async Task<int> GetNextSubtaskDisplayOrderAsync(int parentTaskId)
    {
        var siblings = await GetSubtasksAsync(parentTaskId);
        return siblings.Count == 0 ? 0 : siblings.Max(item => item.DisplayOrder) + 1;
    }

    private static string SubtaskStatusLabel(SubtaskStatus status) => status switch
    {
        SubtaskStatus.Todo => "Yapilacak",
        SubtaskStatus.InProgress => "Devam ediyor",
        SubtaskStatus.Done => "Tamamlandi",
        _ => status.ToString()
    };

    private static (int Done, int Total) CountSubtaskProgress(IEnumerable<TaskItem> subtasks)
    {
        var list = subtasks.ToList();
        var done = list.Count(item => item.SubtaskStatus == SubtaskStatus.Done);
        return (done, list.Count);
    }

    private async Task IndexTaskDocumentAsync(TaskItem task)
    {
        if (task.ParentTaskId.HasValue)
        {
            _searchIndex.RemoveTask(task.Id);
            return;
        }

        var team = await _unitOfWork.Teams.GetByIdAsync(task.TeamId);
        if (team is null || team.IsPersonal)
        {
            _searchIndex.RemoveTask(task.Id);
            return;
        }

        var column = await _unitOfWork.TeamBoardColumns.GetByIdAsync(task.BoardColumnId);
        _searchIndex.IndexTask(task, team.Name, column?.Title ?? string.Empty);
    }

    private async Task LogActivityAsync(
        int teamId,
        int? taskId,
        int userId,
        TaskActivityAction actionType,
        string? oldValue,
        string? newValue)
    {
        _unitOfWork.TaskActivityLogs.Add(new TaskActivityLog
        {
            TeamId = teamId,
            TaskId = taskId,
            UserId = userId,
            ActionType = actionType,
            OldValue = oldValue,
            NewValue = newValue
        });

        await _unitOfWork.SaveChangesAsync();
    }

    private async Task<TaskDetailDto> MapToDetailDtoAsync(TaskItem task)
    {
        var listDto = await MapToListDtoAsync(task);
        var createdBy = await _unitOfWork.Users.GetByIdAsync(task.CreatedByUserId);

        string? parentTitle = null;
        if (task.ParentTaskId.HasValue)
        {
            parentTitle = (await _unitOfWork.TaskItems.GetByIdAsync(task.ParentTaskId.Value))?.Title;
        }

        List<TaskListDto> subtaskDtos = [];
        if (!task.ParentTaskId.HasValue)
        {
            var subtasks = await GetSubtasksAsync(task.Id);
            foreach (var subtask in subtasks)
            {
                subtaskDtos.Add(await MapToListDtoAsync(subtask));
            }
        }

        return new TaskDetailDto
        {
            Id = listDto.Id,
            TeamId = listDto.TeamId,
            TeamName = listDto.TeamName,
            IsPersonalTeam = listDto.IsPersonalTeam,
            BoardId = listDto.BoardId,
            BoardName = listDto.BoardName,
            BoardColumnId = listDto.BoardColumnId,
            BoardColumnTitle = listDto.BoardColumnTitle ?? string.Empty,
            Title = listDto.Title,
            Description = listDto.Description,
            CategoryId = listDto.CategoryId,
            CategoryName = listDto.CategoryName,
            Priority = listDto.Priority,
            CreatedDate = task.CreatedDate,
            StartDate = listDto.StartDate,
            DueDate = listDto.DueDate,
            CompletedAt = listDto.CompletedAt,
            IsCompleted = listDto.IsCompleted,
            CreatedByUserId = task.CreatedByUserId,
            CreatedByEmail = createdBy?.Email ?? string.Empty,
            AssignedToUserId = listDto.AssignedToUserId,
            AssignedToEmail = listDto.AssignedToEmail,
            AssignmentStatus = listDto.AssignmentStatus,
            ParentTaskId = listDto.ParentTaskId,
            ParentTaskTitle = parentTitle,
            SubtaskStatus = listDto.SubtaskStatus,
            SubtaskDoneCount = listDto.SubtaskDoneCount,
            SubtaskTotal = listDto.SubtaskTotal,
            Subtasks = subtaskDtos
        };
    }

    private async Task<TaskListDto> MapToListDtoAsync(TaskItem task)
    {
        string? categoryName = null;
        if (task.CategoryId.HasValue)
        {
            categoryName = (await _unitOfWork.Categories.GetByIdAsync(task.CategoryId.Value))?.Name;
        }

        string? boardColumnTitle = null;
        string? boardName = null;
        string? assignedToEmail = null;
        var column = await _unitOfWork.TeamBoardColumns.GetByIdAsync(task.BoardColumnId);
        if (column is not null)
        {
            boardColumnTitle = column.Title;
        }

        var board = await _unitOfWork.Boards.GetByIdAsync(task.BoardId);
        if (board is not null)
        {
            boardName = board.Name;
        }

        if (task.AssignedToUserId.HasValue)
        {
            assignedToEmail = (await _unitOfWork.Users.GetByIdAsync(task.AssignedToUserId.Value))?.Email;
        }

        var team = await _unitOfWork.Teams.GetByIdAsync(task.TeamId);

        var doneCount = 0;
        var total = 0;
        if (!task.ParentTaskId.HasValue)
        {
            var progress = CountSubtaskProgress(await GetSubtasksAsync(task.Id));
            doneCount = progress.Done;
            total = progress.Total;
        }

        return new TaskListDto
        {
            Id = task.Id,
            Title = task.Title,
            Description = task.Description,
            CategoryId = task.CategoryId,
            CategoryName = categoryName,
            Priority = task.Priority,
            StartDate = task.StartDate,
            DueDate = task.DueDate,
            CompletedAt = task.CompletedAt,
            IsCompleted = task.IsCompleted,
            TeamId = task.TeamId,
            TeamName = team?.Name,
            IsPersonalTeam = team?.IsPersonal ?? false,
            BoardId = task.BoardId,
            BoardName = boardName,
            BoardColumnId = task.BoardColumnId,
            DisplayOrder = task.DisplayOrder,
            BoardColumnTitle = boardColumnTitle,
            AssignedToUserId = task.AssignedToUserId,
            AssignedToEmail = assignedToEmail,
            AssignmentStatus = task.AssignmentStatus,
            ParentTaskId = task.ParentTaskId,
            SubtaskStatus = task.SubtaskStatus,
            SubtaskDoneCount = doneCount,
            SubtaskTotal = total
        };
    }

    private static void ApplyAssignmentState(TaskItem task, int? assignedToUserId, int actorUserId)
    {
        if (!assignedToUserId.HasValue)
        {
            task.AssignmentStatus = AssignmentStatus.None;
            return;
        }

        if (assignedToUserId.Value == actorUserId)
        {
            task.AssignmentStatus = AssignmentStatus.Accepted;
            return;
        }

        task.AssignmentStatus = AssignmentStatus.Pending;
    }

    private static void MarkAssignmentAccepted(TaskItem task)
    {
        task.AssignmentStatus = AssignmentStatus.Accepted;
        task.StartDate = DateTime.UtcNow;
    }

    private static TeamBoardColumn? FindInProgressColumn(IReadOnlyList<TeamBoardColumn> columns)
    {
        var inProgress = columns.FirstOrDefault(column =>
            !column.IsCompletedColumn
            && column.Title.Contains("progress", StringComparison.OrdinalIgnoreCase));

        if (inProgress is not null)
        {
            return inProgress;
        }

        var activeColumns = columns.Where(column => !column.IsCompletedColumn).ToList();
        return activeColumns.Count > 1 ? activeColumns[1] : activeColumns.FirstOrDefault();
    }

    private static TeamBoardColumn? FindTodoColumn(IReadOnlyList<TeamBoardColumn> columns)
    {
        var todo = columns.FirstOrDefault(column =>
            !column.IsCompletedColumn
            && (column.Title.Contains("yapilacak", StringComparison.OrdinalIgnoreCase)
                || column.Title.Contains("yapılacak", StringComparison.OrdinalIgnoreCase)
                || column.Title.Contains("todo", StringComparison.OrdinalIgnoreCase)
                || column.Title.Contains("tum", StringComparison.OrdinalIgnoreCase)
                || column.Title.Contains("tüm", StringComparison.OrdinalIgnoreCase)));

        if (todo is not null)
        {
            return todo;
        }

        return columns.FirstOrDefault(column => !column.IsCompletedColumn);
    }

    private async Task<ServiceResult<int>> ResolveCategoryIdAsync(int? categoryId)
    {
        if (categoryId is > 0)
        {
            var category = await _unitOfWork.Categories.GetByIdAsync(categoryId.Value);
            if (category is null)
            {
                return ServiceResult<int>.Fail(
                    $"Gecersiz categoryId: {categoryId}. Gecerli bir kategori secin.");
            }

            return ServiceResult<int>.Ok(categoryId.Value);
        }

        var otherCategoryId = await _categoryService.GetOtherCategoryIdAsync();
        if (!otherCategoryId.HasValue)
        {
            return ServiceResult<int>.Fail("'Diger' kategorisi bulunamadi.");
        }

        return ServiceResult<int>.Ok(otherCategoryId.Value);
    }
}
