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
    private readonly IFileStorageService _fileStorage;
    private readonly ILuceneSearchIndex _searchIndex;
    private readonly NotificationDispatchService _notificationDispatch;

    public TaskService(
        IUnitOfWork unitOfWork,
        ICategoryService categoryService,
        ITeamService teamService,
        ITeamBoardNotifier boardNotifier,
        IFileStorageService fileStorage,
        ILuceneSearchIndex searchIndex,
        NotificationDispatchService notificationDispatch)
    {
        _unitOfWork = unitOfWork;
        _categoryService = categoryService;
        _teamService = teamService;
        _boardNotifier = boardNotifier;
        _fileStorage = fileStorage;
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

    public async Task<ServiceResult<TaskListDto>> MoveTaskToColumnAsync(int taskId, int boardColumnId, int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<TaskListDto>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var task = taskResult.Data!;
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

        return await ApplyColumnChangeAsync(task, newColumn, userId);
    }

    public async Task<ServiceResult<TaskListDto>> CompleteTaskAsync(int taskId, int userId)
    {
        var taskResult = await GetTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<TaskListDto>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var task = taskResult.Data!;
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
        var columns = await GetBoardColumnsAsync(task.BoardId);
        var activeColumn = columns.FirstOrDefault(column => !column.IsCompletedColumn);
        if (activeColumn is null)
        {
            return ServiceResult<TaskListDto>.Fail("Bu gorevin panosunda aktif sutun bulunamadi.");
        }

        return await ApplyColumnChangeAsync(task, activeColumn, userId);
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
        var title = task.Title;

        var comments = (await _unitOfWork.TaskComments.GetAllAsync())
            .Where(comment => comment.TaskId == taskId)
            .ToList();
        var commentIds = comments.Select(comment => comment.Id).ToHashSet();

        var commentAttachments = (await _unitOfWork.CommentAttachments.GetAllAsync())
            .Where(attachment => commentIds.Contains(attachment.CommentId))
            .ToList();

        var taskAttachments = (await _unitOfWork.TaskAttachments.GetAllAsync())
            .Where(attachment => attachment.TaskId == taskId)
            .ToList();

        var objectKeys = commentAttachments
            .Select(attachment => attachment.ObjectKey)
            .Concat(taskAttachments.Select(attachment => attachment.ObjectKey))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var attachment in commentAttachments)
        {
            await _unitOfWork.CommentAttachments.DeleteAsync(attachment.Id);
        }

        foreach (var comment in OrderCommentsForDelete(comments))
        {
            await _unitOfWork.TaskComments.DeleteAsync(comment.Id);
        }

        foreach (var attachment in taskAttachments)
        {
            await _unitOfWork.TaskAttachments.DeleteAsync(attachment.Id);
        }

        // TaskActivityLogs.TaskId FK NoAction: silmeden once baglantiyi kopar.
        var activityLogs = (await _unitOfWork.TaskActivityLogs.GetAllAsync())
            .Where(log => log.TaskId == taskId)
            .ToList();
        foreach (var log in activityLogs)
        {
            log.TaskId = null;
            _unitOfWork.TaskActivityLogs.Update(log);
        }

        await _unitOfWork.SaveChangesAsync();

        await LogActivityAsync(teamId, null, userId, TaskActivityAction.Deleted, title, null);

        await _unitOfWork.TaskItems.DeleteAsync(taskId);
        await _unitOfWork.SaveChangesAsync();

        foreach (var objectKey in objectKeys)
        {
            try
            {
                await _fileStorage.DeleteAsync(objectKey);
            }
            catch
            {
                // DB silme basarili; MinIO temizligi best-effort.
            }
        }

        await _boardNotifier.NotifyBoardChangedAsync(teamId, TeamBoardChangeTypes.TaskDeleted, userId, taskId, boardId);
        _searchIndex.RemoveTask(taskId);
        return ServiceResult.Ok();
    }

    private async Task<ServiceResult<TaskItem>> GetTaskIfMemberAsync(int taskId, int userId)
    {
        var task = await _unitOfWork.TaskItems.GetByIdAsync(taskId);
        if (task is null)
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
        int userId)
    {
        var oldColumn = await _unitOfWork.TeamBoardColumns.GetByIdAsync(task.BoardColumnId);
        var oldTitle = oldColumn?.Title;

        task.BoardColumnId = newColumn.Id;
        task.IsCompleted = newColumn.IsCompletedColumn;

        _unitOfWork.TaskItems.Update(task);
        await _unitOfWork.SaveChangesAsync();

        await LogActivityAsync(
            task.TeamId,
            task.Id,
            userId,
            TaskActivityAction.ColumnChanged,
            oldTitle,
            newColumn.Title);

        await _boardNotifier.NotifyBoardChangedAsync(task.TeamId, TeamBoardChangeTypes.TaskMoved, userId, task.Id, task.BoardId);
        await IndexTaskDocumentAsync(task);

        return ServiceResult<TaskListDto>.Ok(await MapToListDtoAsync(task));
    }

    private async Task IndexTaskDocumentAsync(TaskItem task)
    {
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

    private static List<TaskComment> OrderCommentsForDelete(IReadOnlyList<TaskComment> comments)
    {
        var byParent = comments
            .Where(comment => comment.ParentCommentId.HasValue)
            .GroupBy(comment => comment.ParentCommentId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var ordered = new List<TaskComment>();
        var visited = new HashSet<int>();

        void Visit(TaskComment comment)
        {
            if (!visited.Add(comment.Id))
            {
                return;
            }

            if (byParent.TryGetValue(comment.Id, out var children))
            {
                foreach (var child in children)
                {
                    Visit(child);
                }
            }

            ordered.Add(comment);
        }

        foreach (var root in comments.Where(comment => !comment.ParentCommentId.HasValue))
        {
            Visit(root);
        }

        foreach (var orphan in comments.Where(comment => !visited.Contains(comment.Id)))
        {
            Visit(orphan);
        }

        return ordered;
    }

    private async Task<TaskDetailDto> MapToDetailDtoAsync(TaskItem task)
    {
        var listDto = await MapToListDtoAsync(task);
        var createdBy = await _unitOfWork.Users.GetByIdAsync(task.CreatedByUserId);

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
            IsCompleted = listDto.IsCompleted,
            CreatedByUserId = task.CreatedByUserId,
            CreatedByEmail = createdBy?.Email ?? string.Empty,
            AssignedToUserId = listDto.AssignedToUserId,
            AssignedToEmail = listDto.AssignedToEmail,
            AssignmentStatus = listDto.AssignmentStatus
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
            IsCompleted = task.IsCompleted,
            TeamId = task.TeamId,
            TeamName = team?.Name,
            IsPersonalTeam = team?.IsPersonal ?? false,
            BoardId = task.BoardId,
            BoardName = boardName,
            BoardColumnId = task.BoardColumnId,
            BoardColumnTitle = boardColumnTitle,
            AssignedToUserId = task.AssignedToUserId,
            AssignedToEmail = assignedToEmail,
            AssignmentStatus = task.AssignmentStatus
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
