using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Todoo.Business.Abstract;
using Todoo.Business.Models;
using Todoo.Business.Models.Sprints;
using Todoo.DataAccess.UnitOfWork;
using Todoo.Entities.Entities;
using Todoo.Entities.Enums;

namespace Todoo.Business.Concrete;

public class SprintService : ISprintService
{
    private static readonly TimeSpan MinSprintDuration = TimeSpan.FromDays(7);
    private static readonly TimeSpan MaxSprintDuration = TimeSpan.FromDays(28);

    private readonly IUnitOfWork _unitOfWork;
    private readonly ISprintAuditSearchService _auditSearch;
    private readonly ILogger<SprintService> _logger;

    public SprintService(
        IUnitOfWork unitOfWork,
        ISprintAuditSearchService auditSearch,
        ILogger<SprintService> logger)
    {
        _unitOfWork = unitOfWork;
        _auditSearch = auditSearch;
        _logger = logger;
    }

    public async Task<ServiceResult<BoardKapsamDto>> GetKapsamAsync(int teamId, int boardId, int userId)
    {
        var access = await EnsureBoardMemberAsync(teamId, boardId, userId);
        if (!access.Success)
        {
            return ServiceResult<BoardKapsamDto>.Fail(
                access.ErrorMessage!,
                access.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var (team, board) = access.Data!;
        var sprints = (await _unitOfWork.Sprints.GetAllAsync())
            .Where(sprint => sprint.BoardId == boardId)
            .OrderBy(sprint => sprint.DisplayOrder)
            .ThenBy(sprint => sprint.Id)
            .ToList();

        var rootTasks = (await _unitOfWork.TaskItems.GetAllAsync())
            .Where(task => task.BoardId == boardId && task.ParentTaskId == null)
            .ToList();

        var subtasks = (await _unitOfWork.TaskItems.GetAllAsync())
            .Where(task => task.BoardId == boardId && task.ParentTaskId.HasValue)
            .GroupBy(task => task.ParentTaskId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var columns = (await _unitOfWork.TeamBoardColumns.GetAllAsync())
            .Where(column => column.BoardId == boardId)
            .ToDictionary(column => column.Id, column => column);
        var users = (await _unitOfWork.Users.GetAllAsync())
            .ToDictionary(user => user.Id, user => user.Email);

        var backlog = rootTasks
            .Where(task => task.SprintId == null)
            .OrderBy(task => task.SprintOrder)
            .ThenBy(task => task.Id)
            .Select(task => MapTask(task, columns, users, subtasks))
            .ToList();

        var sprintDtos = sprints.Select(sprint =>
        {
            var tasks = rootTasks
                .Where(task => task.SprintId == sprint.Id)
                .OrderBy(task => task.SprintOrder)
                .ThenBy(task => task.Id)
                .Select(task => MapTask(task, columns, users, subtasks))
                .ToList();

            return MapSprintDetail(sprint, tasks);
        }).ToList();

        return ServiceResult<BoardKapsamDto>.Ok(new BoardKapsamDto
        {
            TeamId = team.Id,
            TeamName = team.Name,
            BoardId = board.Id,
            BoardName = board.Name,
            BacklogTasks = backlog,
            Sprints = sprintDtos
        });
    }

    public async Task<ServiceResult<SprintDetailDto>> GetByIdAsync(int sprintId, int userId)
    {
        var sprintResult = await GetSprintIfMemberAsync(sprintId, userId);
        if (!sprintResult.Success)
        {
            return ServiceResult<SprintDetailDto>.Fail(
                sprintResult.ErrorMessage!,
                sprintResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var sprint = sprintResult.Data!;
        var kapsam = await GetKapsamAsync(sprint.TeamId, sprint.BoardId, userId);
        if (!kapsam.Success)
        {
            return ServiceResult<SprintDetailDto>.Fail(
                kapsam.ErrorMessage!,
                kapsam.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var detail = kapsam.Data!.Sprints.FirstOrDefault(item => item.Id == sprintId);
        if (detail is null)
        {
            return ServiceResult<SprintDetailDto>.Fail("Sprint bulunamadi.", ServiceErrorKind.NotFound);
        }

        return ServiceResult<SprintDetailDto>.Ok(detail);
    }

    public async Task<ServiceResult<SprintDetailDto>> CreateAsync(
        int teamId,
        int boardId,
        CreateSprintRequest request,
        int userId)
    {
        var access = await EnsureBoardMemberAsync(teamId, boardId, userId);
        if (!access.Success)
        {
            return ServiceResult<SprintDetailDto>.Fail(
                access.ErrorMessage!,
                access.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var dates = ValidateSprintDates(request.PlannedStartDate, request.PlannedEndDate);
        if (!dates.Success)
        {
            return ServiceResult<SprintDetailDto>.Fail(dates.ErrorMessage!);
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceResult<SprintDetailDto>.Fail("Sprint adi zorunludur.");
        }

        var (team, board) = access.Data!;
        var existing = (await _unitOfWork.Sprints.GetAllAsync())
            .Where(sprint => sprint.BoardId == boardId)
            .ToList();
        var nextOrder = existing.Count == 0 ? 0 : existing.Max(sprint => sprint.DisplayOrder) + 1;

        var sprint = new Sprint
        {
            TeamId = team.Id,
            BoardId = board.Id,
            Name = name,
            Goal = string.IsNullOrWhiteSpace(request.Goal) ? null : request.Goal.Trim(),
            Status = SprintStatus.Planned,
            PlannedStartDate = request.PlannedStartDate.ToUniversalTime(),
            PlannedEndDate = request.PlannedEndDate.ToUniversalTime(),
            DisplayOrder = nextOrder,
            CreatedByUserId = userId,
            CreatedDate = DateTime.UtcNow
        };

        _unitOfWork.Sprints.Add(sprint);
        await _unitOfWork.SaveChangesAsync();
        await LogAsync(sprint, userId, SprintActivityAction.SprintCreated, null, sprint.Name, null);

        return await GetByIdAsync(sprint.Id, userId);
    }

    public async Task<ServiceResult<SprintDetailDto>> UpdateAsync(int sprintId, UpdateSprintRequest request, int userId)
    {
        var sprintResult = await GetSprintIfMemberAsync(sprintId, userId);
        if (!sprintResult.Success)
        {
            return ServiceResult<SprintDetailDto>.Fail(
                sprintResult.ErrorMessage!,
                sprintResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var sprint = sprintResult.Data!;
        if (sprint.Status == SprintStatus.Completed)
        {
            return ServiceResult<SprintDetailDto>.Fail("Tamamlanmis sprint duzenlenemez.");
        }

        if (sprint.Status == SprintStatus.Cancelled)
        {
            return ServiceResult<SprintDetailDto>.Fail("Iptal edilmis sprint duzenlenemez.");
        }

        var dates = ValidateSprintDates(request.PlannedStartDate, request.PlannedEndDate);
        if (!dates.Success)
        {
            return ServiceResult<SprintDetailDto>.Fail(dates.ErrorMessage!);
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceResult<SprintDetailDto>.Fail("Sprint adi zorunludur.");
        }

        var oldName = sprint.Name;
        sprint.Name = name;
        sprint.Goal = string.IsNullOrWhiteSpace(request.Goal) ? null : request.Goal.Trim();
        sprint.PlannedStartDate = request.PlannedStartDate.ToUniversalTime();
        sprint.PlannedEndDate = request.PlannedEndDate.ToUniversalTime();

        _unitOfWork.Sprints.Update(sprint);
        await _unitOfWork.SaveChangesAsync();
        await LogAsync(sprint, userId, SprintActivityAction.SprintUpdated, oldName, sprint.Name, null);

        return await GetByIdAsync(sprint.Id, userId);
    }

    public async Task<ServiceResult> DeleteAsync(int sprintId, int userId)
    {
        var sprintResult = await GetSprintIfMemberAsync(sprintId, userId);
        if (!sprintResult.Success)
        {
            return ServiceResult.Fail(
                sprintResult.ErrorMessage!,
                sprintResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var sprint = sprintResult.Data!;
        if (sprint.Status != SprintStatus.Planned)
        {
            return ServiceResult.Fail("Yalnizca planlanmis sprintler silinebilir.");
        }

        var tasks = await GetSprintRootTasksAsync(sprint.Id);
        var nextOrder = await GetNextBacklogOrderAsync(sprint.BoardId);
        foreach (var task in tasks)
        {
            task.SprintId = null;
            task.SprintOrder = nextOrder++;
            _unitOfWork.TaskItems.Update(task);
        }

        await LogAsync(sprint, userId, SprintActivityAction.SprintDeleted, sprint.Name, null, null);

        var logs = (await _unitOfWork.SprintActivityLogs.GetAllAsync())
            .Where(log => log.SprintId == sprint.Id)
            .ToList();
        foreach (var log in logs)
        {
            await _unitOfWork.SprintActivityLogs.DeleteAsync(log.Id);
        }

        await _unitOfWork.Sprints.DeleteAsync(sprint.Id);
        await _unitOfWork.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<SprintTaskDto>> MoveTaskToSprintAsync(
        int sprintId,
        int taskId,
        MoveTaskToSprintRequest request,
        int userId)
    {
        var sprintResult = await GetSprintIfMemberAsync(sprintId, userId);
        if (!sprintResult.Success)
        {
            return ServiceResult<SprintTaskDto>.Fail(
                sprintResult.ErrorMessage!,
                sprintResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var sprint = sprintResult.Data!;
        if (sprint.Status is SprintStatus.Completed or SprintStatus.Cancelled)
        {
            return ServiceResult<SprintTaskDto>.Fail("Bu sprint'e gorev eklenemez.");
        }

        var taskResult = await GetRootTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult<SprintTaskDto>.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var task = taskResult.Data!;
        if (task.BoardId != sprint.BoardId)
        {
            return ServiceResult<SprintTaskDto>.Fail("Gorev ve sprint ayni panoya ait olmalidir.");
        }

        var previousSprintId = task.SprintId;
        if (previousSprintId == sprint.Id)
        {
            await ReorderWithinSprintAsync(sprint.Id, task.Id, request.TargetIndex);
            await _unitOfWork.SaveChangesAsync();
            return ServiceResult<SprintTaskDto>.Ok(await MapSingleTaskAsync(task.Id));
        }

        if (previousSprintId.HasValue)
        {
            var previous = await _unitOfWork.Sprints.GetByIdAsync(previousSprintId.Value);
            if (previous?.Status is SprintStatus.Completed or SprintStatus.Cancelled)
            {
                return ServiceResult<SprintTaskDto>.Fail("Tamamlanmis/iptal sprintten gorev tasinamadi.");
            }
        }

        await DetachFromCurrentSprintAsync(task);
        var siblings = await GetSprintRootTasksAsync(sprint.Id);
        var insertAt = NormalizeIndex(request.TargetIndex, siblings.Count);
        ShiftOrders(siblings, insertAt, 1);
        task.SprintId = sprint.Id;
        task.SprintOrder = insertAt;
        _unitOfWork.TaskItems.Update(task);
        await _unitOfWork.SaveChangesAsync();

        if (sprint.Status == SprintStatus.Active)
        {
            await LogAsync(
                sprint,
                userId,
                SprintActivityAction.TaskAddedAfterSprintStart,
                previousSprintId?.ToString(),
                task.Id.ToString(),
                task.Id);
            await LogAsync(
                sprint,
                userId,
                SprintActivityAction.SprintScopeChanged,
                null,
                $"Task {task.Id} added",
                task.Id);
        }

        return ServiceResult<SprintTaskDto>.Ok(await MapSingleTaskAsync(task.Id));
    }

    public async Task<ServiceResult> MoveTaskToBacklogAsync(int taskId, int? targetIndex, int userId)
    {
        var taskResult = await GetRootTaskIfMemberAsync(taskId, userId);
        if (!taskResult.Success)
        {
            return ServiceResult.Fail(
                taskResult.ErrorMessage!,
                taskResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var task = taskResult.Data!;
        if (!task.SprintId.HasValue)
        {
            await ReorderWithinBacklogAsync(task.BoardId, task.Id, targetIndex);
            await _unitOfWork.SaveChangesAsync();
            return ServiceResult.Ok();
        }

        var sprint = await _unitOfWork.Sprints.GetByIdAsync(task.SprintId.Value);
        if (sprint is null)
        {
            return ServiceResult.Fail("Sprint bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (sprint.Status is SprintStatus.Completed or SprintStatus.Cancelled)
        {
            return ServiceResult.Fail("Bu sprintten gorev cikarilamaz.");
        }

        var wasActive = sprint.Status == SprintStatus.Active;
        await DetachFromCurrentSprintAsync(task);

        var backlog = await GetBacklogTasksAsync(task.BoardId);
        var insertAt = NormalizeIndex(targetIndex, backlog.Count);
        ShiftOrders(backlog, insertAt, 1);
        task.SprintId = null;
        task.SprintOrder = insertAt;
        _unitOfWork.TaskItems.Update(task);
        await _unitOfWork.SaveChangesAsync();

        if (wasActive)
        {
            await LogAsync(
                sprint,
                userId,
                SprintActivityAction.TaskRemovedAfterSprintStart,
                task.Id.ToString(),
                "backlog",
                task.Id);
            await LogAsync(
                sprint,
                userId,
                SprintActivityAction.SprintScopeChanged,
                null,
                $"Task {task.Id} removed to backlog",
                task.Id);
        }

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ReorderSprintTasksAsync(
        int sprintId,
        ReorderSprintTasksRequest request,
        int userId)
    {
        var sprintResult = await GetSprintIfMemberAsync(sprintId, userId);
        if (!sprintResult.Success)
        {
            return ServiceResult.Fail(
                sprintResult.ErrorMessage!,
                sprintResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var sprint = sprintResult.Data!;
        if (sprint.Status is SprintStatus.Completed or SprintStatus.Cancelled)
        {
            return ServiceResult.Fail("Bu sprintte siralama yapilamaz.");
        }

        var tasks = await GetSprintRootTasksAsync(sprint.Id);
        return await ApplyReorderAsync(tasks, request.TaskIds);
    }

    public async Task<ServiceResult> ReorderBacklogAsync(
        int boardId,
        ReorderSprintTasksRequest request,
        int userId)
    {
        var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
        if (board is null)
        {
            return ServiceResult.Fail("Pano bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await IsTeamMemberAsync(board.TeamId, userId))
        {
            return ServiceResult.Fail("Bu panoya erisim yetkiniz yok.", ServiceErrorKind.Forbidden);
        }

        var tasks = await GetBacklogTasksAsync(boardId);
        return await ApplyReorderAsync(tasks, request.TaskIds);
    }

    public async Task<ServiceResult<SprintDetailDto>> StartAsync(int sprintId, int userId)
    {
        var sprintResult = await GetSprintIfMemberAsync(sprintId, userId);
        if (!sprintResult.Success)
        {
            return ServiceResult<SprintDetailDto>.Fail(
                sprintResult.ErrorMessage!,
                sprintResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var sprint = sprintResult.Data!;
        if (sprint.Status != SprintStatus.Planned)
        {
            return ServiceResult<SprintDetailDto>.Fail("Yalnizca planlanmis sprint baslatilabilir.");
        }

        var taskCount = (await GetSprintRootTasksAsync(sprint.Id)).Count;
        if (taskCount == 0)
        {
            return ServiceResult<SprintDetailDto>.Fail("Bos sprint baslatilamaz. Once gorev ekleyin.");
        }

        var otherActive = (await _unitOfWork.Sprints.GetAllAsync())
            .FirstOrDefault(item => item.BoardId == sprint.BoardId
                && item.Id != sprint.Id
                && item.Status == SprintStatus.Active);
        if (otherActive is not null)
        {
            return ServiceResult<SprintDetailDto>.Fail(
                $"Bu panoda zaten aktif bir sprint var: \"{otherActive.Name}\". Önce onu tamamlayın veya iptal edin.");
        }

        sprint.Status = SprintStatus.Active;
        sprint.ActualStartDate = DateTime.UtcNow;
        _unitOfWork.Sprints.Update(sprint);

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return ServiceResult<SprintDetailDto>.Fail(
                "Bu panoda baska bir aktif sprint var. Ayni anda yalnizca bir sprint baslatilabilir.");
        }

        await LogAsync(sprint, userId, SprintActivityAction.SprintStarted, SprintStatus.Planned.ToString(), SprintStatus.Active.ToString(), null);
        return await GetByIdAsync(sprint.Id, userId);
    }

    public async Task<ServiceResult<SprintDetailDto>> CompleteAsync(
        int sprintId,
        CompleteSprintRequest request,
        int userId)
    {
        var sprintResult = await GetSprintIfMemberAsync(sprintId, userId);
        if (!sprintResult.Success)
        {
            return ServiceResult<SprintDetailDto>.Fail(
                sprintResult.ErrorMessage!,
                sprintResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var sprint = sprintResult.Data!;
        if (sprint.Status != SprintStatus.Active)
        {
            return ServiceResult<SprintDetailDto>.Fail("Yalnizca aktif sprint tamamlanabilir.");
        }

        var destination = (request.IncompleteDestination ?? "backlog").Trim().ToLowerInvariant();
        Sprint? targetSprint = null;
        if (destination == "sprint")
        {
            if (!request.TargetSprintId.HasValue)
            {
                return ServiceResult<SprintDetailDto>.Fail("Hedef sprint secilmelidir.");
            }

            targetSprint = await _unitOfWork.Sprints.GetByIdAsync(request.TargetSprintId.Value);
            if (targetSprint is null
                || targetSprint.BoardId != sprint.BoardId
                || targetSprint.Status != SprintStatus.Planned)
            {
                return ServiceResult<SprintDetailDto>.Fail("Hedef, ayni panodaki planlanmis bir sprint olmalidir.");
            }
        }
        else if (destination != "backlog")
        {
            return ServiceResult<SprintDetailDto>.Fail("Gecersiz hedef. backlog veya sprint secin.");
        }

        var tasks = await GetSprintRootTasksAsync(sprint.Id);
        var incomplete = tasks.Where(task => !task.IsCompleted).ToList();

        if (destination == "backlog")
        {
            var nextOrder = await GetNextBacklogOrderAsync(sprint.BoardId);
            foreach (var task in incomplete)
            {
                task.SprintId = null;
                task.SprintOrder = nextOrder++;
                _unitOfWork.TaskItems.Update(task);
            }
        }
        else
        {
            var nextOrder = await GetNextSprintOrderAsync(targetSprint!.Id);
            foreach (var task in incomplete)
            {
                task.SprintId = targetSprint.Id;
                task.SprintOrder = nextOrder++;
                _unitOfWork.TaskItems.Update(task);
            }
        }

        sprint.Status = SprintStatus.Completed;
        sprint.ActualEndDate = DateTime.UtcNow;
        _unitOfWork.Sprints.Update(sprint);
        await _unitOfWork.SaveChangesAsync();

        await LogAsync(
            sprint,
            userId,
            SprintActivityAction.SprintCompleted,
            SprintStatus.Active.ToString(),
            SprintStatus.Completed.ToString(),
            null);

        return await GetByIdAsync(sprint.Id, userId);
    }

    public async Task<ServiceResult<SprintDetailDto>> CancelAsync(
        int sprintId,
        CancelSprintRequest request,
        int userId)
    {
        var sprintResult = await GetSprintIfMemberAsync(sprintId, userId);
        if (!sprintResult.Success)
        {
            return ServiceResult<SprintDetailDto>.Fail(
                sprintResult.ErrorMessage!,
                sprintResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var sprint = sprintResult.Data!;
        if (sprint.Status is SprintStatus.Completed or SprintStatus.Cancelled)
        {
            return ServiceResult<SprintDetailDto>.Fail("Bu sprint iptal edilemez.");
        }

        var destination = (request.TaskDestination ?? "backlog").Trim().ToLowerInvariant();
        Sprint? targetSprint = null;
        if (destination == "sprint")
        {
            if (!request.TargetSprintId.HasValue)
            {
                return ServiceResult<SprintDetailDto>.Fail("Hedef sprint secilmelidir.");
            }

            targetSprint = await _unitOfWork.Sprints.GetByIdAsync(request.TargetSprintId.Value);
            if (targetSprint is null
                || targetSprint.BoardId != sprint.BoardId
                || targetSprint.Status != SprintStatus.Planned
                || targetSprint.Id == sprint.Id)
            {
                return ServiceResult<SprintDetailDto>.Fail("Hedef, ayni panodaki baska planlanmis sprint olmalidir.");
            }
        }
        else if (destination != "backlog")
        {
            return ServiceResult<SprintDetailDto>.Fail("Gecersiz hedef. backlog veya sprint secin.");
        }

        var wasActive = sprint.Status == SprintStatus.Active;
        var tasks = await GetSprintRootTasksAsync(sprint.Id);
        if (destination == "backlog")
        {
            var nextOrder = await GetNextBacklogOrderAsync(sprint.BoardId);
            foreach (var task in tasks)
            {
                task.SprintId = null;
                task.SprintOrder = nextOrder++;
                _unitOfWork.TaskItems.Update(task);
            }
        }
        else
        {
            var nextOrder = await GetNextSprintOrderAsync(targetSprint!.Id);
            foreach (var task in tasks)
            {
                task.SprintId = targetSprint.Id;
                task.SprintOrder = nextOrder++;
                _unitOfWork.TaskItems.Update(task);
            }
        }

        sprint.Status = SprintStatus.Cancelled;
        sprint.ActualEndDate = DateTime.UtcNow;
        _unitOfWork.Sprints.Update(sprint);
        await _unitOfWork.SaveChangesAsync();

        await LogAsync(
            sprint,
            userId,
            SprintActivityAction.SprintCancelled,
            wasActive ? SprintStatus.Active.ToString() : SprintStatus.Planned.ToString(),
            SprintStatus.Cancelled.ToString(),
            null);

        return await GetByIdAsync(sprint.Id, userId);
    }

    public async Task<ServiceResult<IReadOnlyList<SprintAuditEntryDto>>> GetActivityAsync(
        int sprintId,
        int userId,
        int take = 100)
    {
        var sprintResult = await GetSprintIfMemberAsync(sprintId, userId);
        if (!sprintResult.Success)
        {
            return ServiceResult<IReadOnlyList<SprintAuditEntryDto>>.Fail(
                sprintResult.ErrorMessage!,
                sprintResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var fromSearch = await _auditSearch.SearchBySprintAsync(sprintId, take);
        if (fromSearch.Count > 0)
        {
            return ServiceResult<IReadOnlyList<SprintAuditEntryDto>>.Ok(fromSearch);
        }

        // OpenSearch bos/erisilemezse SQL yedegi
        var sprint = sprintResult.Data!;
        var users = (await _unitOfWork.Users.GetAllAsync())
            .ToDictionary(user => user.Id, user => user.Email);
        var logs = (await _unitOfWork.SprintActivityLogs.GetAllAsync())
            .Where(log => log.SprintId == sprintId)
            .OrderByDescending(log => log.CreatedDate)
            .Take(Math.Clamp(take, 1, 500))
            .Select(log => new SprintAuditEntryDto
            {
                Id = log.Id.ToString(),
                TeamId = log.TeamId,
                BoardId = sprint.BoardId,
                SprintId = log.SprintId,
                SprintName = sprint.Name,
                TaskId = log.TaskId,
                UserId = log.UserId,
                UserEmail = users.GetValueOrDefault(log.UserId),
                ActionType = log.ActionType.ToString(),
                OldValue = log.OldValue,
                NewValue = log.NewValue,
                CreatedDate = log.CreatedDate,
                Source = "sql"
            })
            .ToList();

        return ServiceResult<IReadOnlyList<SprintAuditEntryDto>>.Ok(logs);
    }

    private async Task<ServiceResult<(Team Team, Board Board)>> EnsureBoardMemberAsync(
        int teamId,
        int boardId,
        int userId)
    {
        if (!await IsTeamMemberAsync(teamId, userId))
        {
            return ServiceResult<(Team, Board)>.Fail("Bu takima erisim yetkiniz yok.", ServiceErrorKind.Forbidden);
        }

        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
        if (team is null || board is null || board.TeamId != teamId)
        {
            return ServiceResult<(Team, Board)>.Fail("Pano bulunamadi.", ServiceErrorKind.NotFound);
        }

        return ServiceResult<(Team, Board)>.Ok((team, board));
    }

    private async Task<ServiceResult<Sprint>> GetSprintIfMemberAsync(int sprintId, int userId)
    {
        var sprint = await _unitOfWork.Sprints.GetByIdAsync(sprintId);
        if (sprint is null)
        {
            return ServiceResult<Sprint>.Fail("Sprint bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await IsTeamMemberAsync(sprint.TeamId, userId))
        {
            return ServiceResult<Sprint>.Fail("Bu sprinte erisim yetkiniz yok.", ServiceErrorKind.Forbidden);
        }

        return ServiceResult<Sprint>.Ok(sprint);
    }

    private async Task<ServiceResult<TaskItem>> GetRootTaskIfMemberAsync(int taskId, int userId)
    {
        var task = await _unitOfWork.TaskItems.GetByIdAsync(taskId);
        if (task is null)
        {
            return ServiceResult<TaskItem>.Fail("Gorev bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (task.ParentTaskId.HasValue)
        {
            return ServiceResult<TaskItem>.Fail("Alt gorevler sprinte ayrica atanamaz.");
        }

        if (!await IsTeamMemberAsync(task.TeamId, userId))
        {
            return ServiceResult<TaskItem>.Fail("Bu goreve erisim yetkiniz yok.", ServiceErrorKind.Forbidden);
        }

        return ServiceResult<TaskItem>.Ok(task);
    }

    private async Task<bool> IsTeamMemberAsync(int teamId, int userId)
    {
        return (await _unitOfWork.TeamMembers.GetAllAsync())
            .Any(member => member.TeamId == teamId && member.UserId == userId);
    }

    private static ServiceResult ValidateSprintDates(DateTime start, DateTime end)
    {
        var startUtc = start.ToUniversalTime();
        var endUtc = end.ToUniversalTime();
        if (endUtc <= startUtc)
        {
            return ServiceResult.Fail("Sprint bitis tarihi baslangic tarihinden sonra olmalidir.");
        }

        var duration = endUtc - startUtc;
        if (duration < MinSprintDuration || duration > MaxSprintDuration)
        {
            return ServiceResult.Fail("Sprint suresi 1 ile 4 hafta arasinda olmalidir.");
        }

        return ServiceResult.Ok();
    }

    private async Task<List<TaskItem>> GetSprintRootTasksAsync(int sprintId)
    {
        return (await _unitOfWork.TaskItems.GetAllAsync())
            .Where(task => task.SprintId == sprintId && task.ParentTaskId == null)
            .OrderBy(task => task.SprintOrder)
            .ThenBy(task => task.Id)
            .ToList();
    }

    private async Task<List<TaskItem>> GetBacklogTasksAsync(int boardId)
    {
        return (await _unitOfWork.TaskItems.GetAllAsync())
            .Where(task => task.BoardId == boardId && task.ParentTaskId == null && task.SprintId == null)
            .OrderBy(task => task.SprintOrder)
            .ThenBy(task => task.Id)
            .ToList();
    }

    private async Task<int> GetNextSprintOrderAsync(int sprintId)
    {
        var tasks = await GetSprintRootTasksAsync(sprintId);
        return tasks.Count == 0 ? 0 : tasks.Max(task => task.SprintOrder) + 1;
    }

    private async Task<int> GetNextBacklogOrderAsync(int boardId)
    {
        var tasks = await GetBacklogTasksAsync(boardId);
        return tasks.Count == 0 ? 0 : tasks.Max(task => task.SprintOrder) + 1;
    }

    private async Task DetachFromCurrentSprintAsync(TaskItem task)
    {
        if (!task.SprintId.HasValue)
        {
            var backlog = await GetBacklogTasksAsync(task.BoardId);
            CompactOrders(backlog.Where(item => item.Id != task.Id).ToList());
            return;
        }

        var siblings = await GetSprintRootTasksAsync(task.SprintId.Value);
        CompactOrders(siblings.Where(item => item.Id != task.Id).ToList());
        task.SprintId = null;
    }

    private async Task ReorderWithinSprintAsync(int sprintId, int taskId, int? targetIndex)
    {
        var tasks = await GetSprintRootTasksAsync(sprintId);
        MoveInList(tasks, taskId, targetIndex);
    }

    private async Task ReorderWithinBacklogAsync(int boardId, int taskId, int? targetIndex)
    {
        var tasks = await GetBacklogTasksAsync(boardId);
        MoveInList(tasks, taskId, targetIndex);
    }

    private void MoveInList(List<TaskItem> tasks, int taskId, int? targetIndex)
    {
        var current = tasks.FindIndex(task => task.Id == taskId);
        if (current < 0)
        {
            return;
        }

        var item = tasks[current];
        tasks.RemoveAt(current);
        var insertAt = NormalizeIndex(targetIndex, tasks.Count);
        tasks.Insert(insertAt, item);
        for (var i = 0; i < tasks.Count; i++)
        {
            if (tasks[i].SprintOrder == i)
            {
                continue;
            }

            tasks[i].SprintOrder = i;
            _unitOfWork.TaskItems.Update(tasks[i]);
        }
    }

    private async Task<ServiceResult> ApplyReorderAsync(List<TaskItem> tasks, List<int> orderedIds)
    {
        if (orderedIds.Count != tasks.Count || orderedIds.Distinct().Count() != orderedIds.Count)
        {
            return ServiceResult.Fail("Siralama listesi gecersiz.");
        }

        var map = tasks.ToDictionary(task => task.Id);
        if (orderedIds.Any(id => !map.ContainsKey(id)))
        {
            return ServiceResult.Fail("Siralama listesi bu listedeki gorevlerle eslesmiyor.");
        }

        for (var i = 0; i < orderedIds.Count; i++)
        {
            var task = map[orderedIds[i]];
            if (task.SprintOrder == i)
            {
                continue;
            }

            task.SprintOrder = i;
            _unitOfWork.TaskItems.Update(task);
        }

        await _unitOfWork.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private void CompactOrders(List<TaskItem> tasks)
    {
        for (var i = 0; i < tasks.Count; i++)
        {
            if (tasks[i].SprintOrder == i)
            {
                continue;
            }

            tasks[i].SprintOrder = i;
            _unitOfWork.TaskItems.Update(tasks[i]);
        }
    }

    private void ShiftOrders(List<TaskItem> tasks, int insertAt, int delta)
    {
        foreach (var task in tasks.Where(task => task.SprintOrder >= insertAt))
        {
            task.SprintOrder += delta;
            _unitOfWork.TaskItems.Update(task);
        }
    }

    private static int NormalizeIndex(int? targetIndex, int count)
    {
        if (!targetIndex.HasValue)
        {
            return count;
        }

        return Math.Max(0, Math.Min(targetIndex.Value, count));
    }

    private async Task LogAsync(
        Sprint sprint,
        int userId,
        SprintActivityAction action,
        string? oldValue,
        string? newValue,
        int? taskId)
    {
        var createdAt = DateTime.UtcNow;
        _unitOfWork.SprintActivityLogs.Add(new SprintActivityLog
        {
            TeamId = sprint.TeamId,
            SprintId = sprint.Id,
            TaskId = taskId,
            UserId = userId,
            ActionType = action,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedDate = createdAt
        });
        await _unitOfWork.SaveChangesAsync();

        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        try
        {
            await _auditSearch.IndexAsync(new SprintAuditWriteRequest
            {
                TeamId = sprint.TeamId,
                BoardId = sprint.BoardId,
                SprintId = sprint.Id,
                SprintName = sprint.Name,
                TaskId = taskId,
                UserId = userId,
                UserEmail = user?.Email,
                ActionType = action.ToString(),
                OldValue = oldValue,
                NewValue = newValue,
                CreatedDate = createdAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenSearch dual-write basarisiz; SQL audit kaydi mevcut.");
        }
    }

    private static SprintDetailDto MapSprintDetail(Sprint sprint, List<SprintTaskDto> tasks)
    {
        return new SprintDetailDto
        {
            Id = sprint.Id,
            TeamId = sprint.TeamId,
            BoardId = sprint.BoardId,
            Name = sprint.Name,
            Goal = sprint.Goal,
            Status = sprint.Status,
            PlannedStartDate = sprint.PlannedStartDate,
            PlannedEndDate = sprint.PlannedEndDate,
            ActualStartDate = sprint.ActualStartDate,
            ActualEndDate = sprint.ActualEndDate,
            DisplayOrder = sprint.DisplayOrder,
            TaskCount = tasks.Count,
            CompletedTaskCount = tasks.Count(task => task.IsCompleted),
            Tasks = tasks
        };
    }

    private static SprintTaskDto MapTask(
        TaskItem task,
        IReadOnlyDictionary<int, TeamBoardColumn> columns,
        IReadOnlyDictionary<int, string> users,
        IReadOnlyDictionary<int, List<TaskItem>> subtasks)
    {
        var children = subtasks.GetValueOrDefault(task.Id) ?? [];
        return new SprintTaskDto
        {
            Id = task.Id,
            Title = task.Title,
            Description = task.Description,
            Priority = task.Priority,
            IsCompleted = task.IsCompleted,
            AssignedToUserId = task.AssignedToUserId,
            AssignedToEmail = task.AssignedToUserId.HasValue
                ? users.GetValueOrDefault(task.AssignedToUserId.Value)
                : null,
            SprintOrder = task.SprintOrder,
            BoardColumnId = task.BoardColumnId,
            BoardColumnTitle = columns.TryGetValue(task.BoardColumnId, out var column) ? column.Title : null,
            SubtaskDoneCount = children.Count(child => child.SubtaskStatus == SubtaskStatus.Done),
            SubtaskTotal = children.Count
        };
    }

    private async Task<SprintTaskDto> MapSingleTaskAsync(int taskId)
    {
        var task = (await _unitOfWork.TaskItems.GetByIdAsync(taskId))!;
        var columns = (await _unitOfWork.TeamBoardColumns.GetAllAsync())
            .Where(column => column.BoardId == task.BoardId)
            .ToDictionary(column => column.Id, column => column);
        var users = (await _unitOfWork.Users.GetAllAsync())
            .ToDictionary(user => user.Id, user => user.Email);
        var subtasks = (await _unitOfWork.TaskItems.GetAllAsync())
            .Where(item => item.ParentTaskId == task.Id)
            .GroupBy(item => item.ParentTaskId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        return MapTask(task, columns, users, subtasks);
    }
}
