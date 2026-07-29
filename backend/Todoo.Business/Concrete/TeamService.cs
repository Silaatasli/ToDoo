using Todoo.Business.Abstract;
using Todoo.Business.Helpers;
using Todoo.Business.Models;
using Todoo.Business.Models.Teams;
using Todoo.DataAccess.UnitOfWork;
using Todoo.Entities.Entities;
using Todoo.Entities.Enums;

namespace Todoo.Business.Concrete;

public class TeamService : ITeamService
{
    private const string DefaultBoardName = "Ana pano";
    private static readonly string[] DefaultColumnTitles = ["All Tasks", "In Progress", "Completed"];

    private readonly IUnitOfWork _unitOfWork;
    private readonly ITeamBoardNotifier _boardNotifier;
    private readonly ILuceneSearchIndex _searchIndex;
    private readonly NotificationDispatchService _notificationDispatch;

    public TeamService(
        IUnitOfWork unitOfWork,
        ITeamBoardNotifier boardNotifier,
        ILuceneSearchIndex searchIndex,
        NotificationDispatchService notificationDispatch)
    {
        _unitOfWork = unitOfWork;
        _boardNotifier = boardNotifier;
        _searchIndex = searchIndex;
        _notificationDispatch = notificationDispatch;
    }

    public async Task<ServiceResult<TeamDetailDto>> CreateTeamAsync(
        string name,
        string? boardName,
        IReadOnlyList<string>? columnTitles,
        int userId)
    {
        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return ServiceResult<TeamDetailDto>.Fail("Takim adi bos olamaz.");
        }

        var resolvedBoardName = string.IsNullOrWhiteSpace(boardName)
            ? DefaultBoardName
            : boardName.Trim();

        var titles = ResolveColumnTitles(columnTitles);
        var team = new Team
        {
            Name = trimmedName,
            LeaderUserId = userId,
            CreatedByUserId = userId,
            IsPersonal = false
        };

        _unitOfWork.Teams.Add(team);
        await _unitOfWork.SaveChangesAsync();

        _unitOfWork.TeamMembers.Add(new TeamMember
        {
            TeamId = team.Id,
            UserId = userId
        });

        var board = new Board
        {
            TeamId = team.Id,
            Name = resolvedBoardName,
            DisplayOrder = 0
        };
        _unitOfWork.Boards.Add(board);
        await _unitOfWork.SaveChangesAsync();

        for (var i = 0; i < titles.Count; i++)
        {
            var isCompletedColumn = titles[i].Contains("complet", StringComparison.OrdinalIgnoreCase)
                || titles[i].Contains("tamam", StringComparison.OrdinalIgnoreCase)
                || (!HasCustomTitles(columnTitles) && titles.Count == 3 && i == 2);

            _unitOfWork.TeamBoardColumns.Add(new TeamBoardColumn
            {
                BoardId = board.Id,
                Title = titles[i],
                DisplayOrder = i,
                IsCompletedColumn = isCompletedColumn
            });
        }

        await _unitOfWork.SaveChangesAsync();
        _searchIndex.IndexTeam(team);
        _searchIndex.IndexBoard(board, team.Name);
        await IndexPersonDocumentAsync(userId);
        return await GetTeamByIdAsync(team.Id, userId);
    }

    public async Task<IEnumerable<TeamListDto>> GetTeamsForUserAsync(int userId)
    {
        var memberTeamIds = (await _unitOfWork.TeamMembers.GetAllAsync())
            .Where(member => member.UserId == userId)
            .Select(member => member.TeamId)
            .ToHashSet();

        var teams = (await _unitOfWork.Teams.GetAllAsync())
            .Where(team => memberTeamIds.Contains(team.Id) && !team.IsPersonal)
            .OrderByDescending(team => team.CreatedDate);

        var users = (await _unitOfWork.Users.GetAllAsync()).ToDictionary(user => user.Id, user => user.Email);
        var memberCounts = (await _unitOfWork.TeamMembers.GetAllAsync())
            .GroupBy(member => member.TeamId)
            .ToDictionary(group => group.Key, group => group.Count());

        return teams.Select(team => new TeamListDto
        {
            Id = team.Id,
            Name = team.Name,
            LeaderUserId = team.LeaderUserId,
            LeaderEmail = users.GetValueOrDefault(team.LeaderUserId, string.Empty),
            MemberCount = memberCounts.GetValueOrDefault(team.Id, 0),
            CreatedDate = team.CreatedDate
        });
    }

    public async Task<ServiceResult<TeamDetailDto>> GetTeamByIdAsync(int teamId, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult<TeamDetailDto>.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await IsTeamMemberAsync(teamId, userId))
        {
            return ServiceResult<TeamDetailDto>.Fail("Bu takimin uyesi degilsiniz.", ServiceErrorKind.Forbidden);
        }

        var users = (await _unitOfWork.Users.GetAllAsync()).ToDictionary(user => user.Id);
        var members = (await _unitOfWork.TeamMembers.GetAllAsync())
            .Where(member => member.TeamId == teamId)
            .OrderBy(member => member.JoinedDate)
            .Select(member =>
            {
                var user = users.GetValueOrDefault(member.UserId);
                return new TeamMemberDto
                {
                    UserId = member.UserId,
                    Email = user?.Email ?? string.Empty,
                    FirstName = user?.FirstName,
                    LastName = user?.LastName,
                    IsLeader = member.UserId == team.LeaderUserId,
                    CanPublishAnnouncements = member.UserId == team.LeaderUserId || member.CanPublishAnnouncements, //duyuru yayınlama yetkisi, lider ise otomatik olarak true, değilse member.CanPublishAnnouncements değerine bakılır
                    JoinedDate = member.JoinedDate,
                    HasProfilePhoto = !string.IsNullOrWhiteSpace(user?.ProfilePhotoObjectKey)
                };
            })
            .ToList();

        var boards = (await _unitOfWork.Boards.GetAllAsync())
            .Where(board => board.TeamId == teamId)
            .OrderBy(board => board.DisplayOrder)
            .ThenBy(board => board.Id)
            .Select(MapBoardListDto)
            .ToList();

        return ServiceResult<TeamDetailDto>.Ok(new TeamDetailDto
        {
            Id = team.Id,
            Name = team.Name,
            LeaderUserId = team.LeaderUserId,
            LeaderEmail = users.GetValueOrDefault(team.LeaderUserId)?.Email ?? string.Empty,
            CreatedDate = team.CreatedDate,
            Members = members,
            Boards = boards,
            BoardColumns = []
        });
    }

    public async Task<ServiceResult<IEnumerable<BoardListDto>>> GetBoardsAsync(int teamId, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult<IEnumerable<BoardListDto>>.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await IsTeamMemberAsync(teamId, userId))
        {
            return ServiceResult<IEnumerable<BoardListDto>>.Fail("Bu takimin uyesi degilsiniz.", ServiceErrorKind.Forbidden);
        }

        var boards = (await _unitOfWork.Boards.GetAllAsync())
            .Where(board => board.TeamId == teamId)
            .OrderBy(board => board.DisplayOrder)
            .ThenBy(board => board.Id)
            .Select(MapBoardListDto);

        return ServiceResult<IEnumerable<BoardListDto>>.Ok(boards);
    }

    public async Task<ServiceResult<BoardListDto>> CreateBoardAsync(
        int teamId,
        string name,
        IReadOnlyList<string>? columnTitles,
        int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult<BoardListDto>.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (team.LeaderUserId != userId)
        {
            return ServiceResult<BoardListDto>.Fail("Sadece takim lideri pano olusturabilir.", ServiceErrorKind.Forbidden);
        }

        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return ServiceResult<BoardListDto>.Fail("Pano adi bos olamaz.");
        }

        var existingBoards = (await _unitOfWork.Boards.GetAllAsync())
            .Where(board => board.TeamId == teamId)
            .ToList();
        var nextOrder = existingBoards.Count == 0 ? 0 : existingBoards.Max(board => board.DisplayOrder) + 1;

        var board = new Board
        {
            TeamId = teamId,
            Name = trimmedName,
            DisplayOrder = nextOrder
        };

        _unitOfWork.Boards.Add(board);
        await _unitOfWork.SaveChangesAsync();

        var titles = ResolveColumnTitles(columnTitles);
        for (var i = 0; i < titles.Count; i++)
        {
            var isCompletedColumn = titles[i].Contains("complet", StringComparison.OrdinalIgnoreCase)
                || titles[i].Contains("tamam", StringComparison.OrdinalIgnoreCase)
                || (!HasCustomTitles(columnTitles) && titles.Count == 3 && i == 2);

            _unitOfWork.TeamBoardColumns.Add(new TeamBoardColumn
            {
                BoardId = board.Id,
                Title = titles[i],
                DisplayOrder = i,
                IsCompletedColumn = isCompletedColumn
            });
        }

        await _unitOfWork.SaveChangesAsync();
        _searchIndex.IndexBoard(board, team.Name);
        await _boardNotifier.NotifyBoardChangedAsync(teamId, TeamBoardChangeTypes.BoardCreated, userId, boardId: board.Id);

        return ServiceResult<BoardListDto>.Ok(MapBoardListDto(board));
    }

    public async Task<ServiceResult> DeleteBoardAsync(int teamId, int boardId, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (team.LeaderUserId != userId)
        {
            return ServiceResult.Fail("Sadece takim lideri pano silebilir.", ServiceErrorKind.Forbidden);
        }

        var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
        if (board is null || board.TeamId != teamId)
        {
            return ServiceResult.Fail("Pano bulunamadi.", ServiceErrorKind.NotFound);
        }

        var boardCount = (await _unitOfWork.Boards.GetAllAsync()).Count(b => b.TeamId == teamId);
        if (boardCount <= 1)
        {
            return ServiceResult.Fail("Takimin son panosu silinemez.");
        }

        var tasks = (await _unitOfWork.TaskItems.GetAllIgnoreFiltersAsync())
            .Where(task => task.BoardId == boardId)
            .ToList();
        var taskIds = tasks.Select(task => task.Id).ToHashSet();

        var activityLogs = (await _unitOfWork.TaskActivityLogs.GetAllAsync())
            .Where(log => log.TaskId.HasValue && taskIds.Contains(log.TaskId.Value))
            .ToList();
        foreach (var log in activityLogs)
        {
            log.TaskId = null;
            _unitOfWork.TaskActivityLogs.Update(log);
        }

        foreach (var task in tasks)
        {
            _searchIndex.RemoveTask(task.Id);
            await _unitOfWork.TaskItems.DeleteAsync(task.Id);
        }

        var columns = (await _unitOfWork.TeamBoardColumns.GetAllAsync())
            .Where(column => column.BoardId == boardId)
            .ToList();

        foreach (var column in columns)
        {
            await _unitOfWork.TeamBoardColumns.DeleteAsync(column.Id);
        }

        await _unitOfWork.Boards.DeleteAsync(boardId);
        await _unitOfWork.SaveChangesAsync();
        _searchIndex.RemoveBoard(boardId);
        await _boardNotifier.NotifyBoardChangedAsync(teamId, TeamBoardChangeTypes.BoardDeleted, userId, boardId: boardId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<TeamBoardDto>> GetBoardAsync(int teamId, int boardId, int userId)
    {
        var teamResult = await GetTeamByIdAsync(teamId, userId);
        if (!teamResult.Success)
        {
            return ServiceResult<TeamBoardDto>.Fail(
                teamResult.ErrorMessage!,
                teamResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
        if (board is null || board.TeamId != teamId)
        {
            return ServiceResult<TeamBoardDto>.Fail("Pano bulunamadi.", ServiceErrorKind.NotFound);
        }

        return await BuildBoardDtoAsync(teamResult.Data!, board);
    }

    public async Task<ServiceResult<TeamBoardDto>> GetTeamBoardAsync(int teamId, int userId)
    {
        var teamResult = await GetTeamByIdAsync(teamId, userId);
        if (!teamResult.Success)
        {
            return ServiceResult<TeamBoardDto>.Fail(
                teamResult.ErrorMessage!,
                teamResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var board = (await _unitOfWork.Boards.GetAllAsync())
            .Where(b => b.TeamId == teamId)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.Id)
            .FirstOrDefault();

        if (board is null)
        {
            return ServiceResult<TeamBoardDto>.Fail("Pano bulunamadi.", ServiceErrorKind.NotFound);
        }

        return await BuildBoardDtoAsync(teamResult.Data!, board);
    }

    public async Task<ServiceResult<TeamBoardColumnDto>> AddBoardColumnAsync(int teamId, int boardId, string title, int userId)
    {
        var accessResult = await EnsureBoardAccessAsync(teamId, boardId, userId);
        if (!accessResult.Success)
        {
            return ServiceResult<TeamBoardColumnDto>.Fail(
                accessResult.ErrorMessage!,
                accessResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var trimmedTitle = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            return ServiceResult<TeamBoardColumnDto>.Fail("Sutun basligi bos olamaz.");
        }

        var columns = (await _unitOfWork.TeamBoardColumns.GetAllAsync())
            .Where(column => column.BoardId == boardId)
            .ToList();

        var nextOrder = columns.Count == 0 ? 0 : columns.Max(column => column.DisplayOrder) + 1;
        var column = new TeamBoardColumn
        {
            BoardId = boardId,
            Title = trimmedTitle,
            DisplayOrder = nextOrder,
            IsCompletedColumn = trimmedTitle.Contains("complet", StringComparison.OrdinalIgnoreCase)
                || trimmedTitle.Contains("tamam", StringComparison.OrdinalIgnoreCase)
        };

        _unitOfWork.TeamBoardColumns.Add(column);
        await _unitOfWork.SaveChangesAsync();
        await _boardNotifier.NotifyBoardChangedAsync(teamId, TeamBoardChangeTypes.ColumnAdded, userId, boardId: boardId);

        return ServiceResult<TeamBoardColumnDto>.Ok(new TeamBoardColumnDto
        {
            Id = column.Id,
            Title = column.Title,
            DisplayOrder = column.DisplayOrder,
            IsCompletedColumn = column.IsCompletedColumn
        });
    }

    public async Task<ServiceResult<TeamBoardColumnDto>> UpdateBoardColumnAsync(int teamId, int boardId, int columnId, string title, int userId)
    {
        var accessResult = await EnsureBoardAccessAsync(teamId, boardId, userId);
        if (!accessResult.Success)
        {
            return ServiceResult<TeamBoardColumnDto>.Fail(
                accessResult.ErrorMessage!,
                accessResult.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var trimmedTitle = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            return ServiceResult<TeamBoardColumnDto>.Fail("Sutun basligi bos olamaz.");
        }

        var column = await _unitOfWork.TeamBoardColumns.GetByIdAsync(columnId);
        if (column is null || column.BoardId != boardId)
        {
            return ServiceResult<TeamBoardColumnDto>.Fail("Sutun bulunamadi.", ServiceErrorKind.NotFound);
        }

        column.Title = trimmedTitle;
        column.IsCompletedColumn = trimmedTitle.Contains("complet", StringComparison.OrdinalIgnoreCase)
            || trimmedTitle.Contains("tamam", StringComparison.OrdinalIgnoreCase);

        _unitOfWork.TeamBoardColumns.Update(column);
        await _unitOfWork.SaveChangesAsync();
        await ReindexTasksInColumnAsync(column);
        await _boardNotifier.NotifyBoardChangedAsync(teamId, TeamBoardChangeTypes.ColumnUpdated, userId, boardId: boardId);

        return ServiceResult<TeamBoardColumnDto>.Ok(new TeamBoardColumnDto
        {
            Id = column.Id,
            Title = column.Title,
            DisplayOrder = column.DisplayOrder,
            IsCompletedColumn = column.IsCompletedColumn
        });
    }

    public async Task<ServiceResult> ReorderBoardColumnsAsync(int teamId, int boardId, IReadOnlyList<int> orderedColumnIds, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (team.LeaderUserId != userId)
        {
            return ServiceResult.Fail("Sadece takim lideri sutun siralayabilir.", ServiceErrorKind.Forbidden);
        }

        var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
        if (board is null || board.TeamId != teamId)
        {
            return ServiceResult.Fail("Pano bulunamadi.", ServiceErrorKind.NotFound);
        }

        var columns = (await _unitOfWork.TeamBoardColumns.GetAllAsync())
            .Where(column => column.BoardId == boardId)
            .OrderBy(column => column.DisplayOrder)
            .ToList();

        if (orderedColumnIds.Count != columns.Count)
        {
            return ServiceResult.Fail("Gecersiz sutun sirasi.");
        }

        var existingIds = columns.Select(column => column.Id).OrderBy(id => id).ToList();
        var requestedIds = orderedColumnIds.OrderBy(id => id).ToList();
        if (!existingIds.SequenceEqual(requestedIds))
        {
            return ServiceResult.Fail("Gecersiz sutun sirasi.");
        }

        var orderMap = orderedColumnIds
            .Select((columnId, index) => new { columnId, index })
            .ToDictionary(item => item.columnId, item => item.index);

        foreach (var column in columns)
        {
            column.DisplayOrder = orderMap[column.Id];
            _unitOfWork.TeamBoardColumns.Update(column);
        }

        await _unitOfWork.SaveChangesAsync();
        await _boardNotifier.NotifyBoardChangedAsync(teamId, TeamBoardChangeTypes.ColumnsReordered, userId, boardId: boardId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteTeamAsync(int teamId, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (team.LeaderUserId != userId)
        {
            return ServiceResult.Fail("Sadece takim lideri takimi silebilir.", ServiceErrorKind.Forbidden);
        }

        var memberUserIds = (await _unitOfWork.TeamMembers.GetAllAsync())
            .Where(member => member.TeamId == teamId)
            .Select(member => member.UserId)
            .Distinct()
            .ToList();

        await _unitOfWork.Teams.DeleteAsync(teamId);
        await _unitOfWork.SaveChangesAsync();

        _searchIndex.RemoveTeam(teamId);
        _searchIndex.RemoveBoardsForTeam(teamId);
        _searchIndex.RemoveTasksForTeam(teamId);
        foreach (var memberUserId in memberUserIds)
        {
            await IndexPersonDocumentAsync(memberUserId);
        }

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> AddMemberAsync(int teamId, string email, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (team.LeaderUserId != userId)
        {
            return ServiceResult.Fail("Sadece takim lideri uye ekleyebilir.", ServiceErrorKind.Forbidden);
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var newMember = (await _unitOfWork.Users.GetAllAsync())
            .FirstOrDefault(user => user.Email == normalizedEmail);

        if (newMember is null)
        {
            return ServiceResult.Fail("Bu e-posta ile kayitli kullanici bulunamadi.");
        }

        var alreadyMember = (await _unitOfWork.TeamMembers.GetAllAsync())
            .Any(member => member.TeamId == teamId && member.UserId == newMember.Id);

        if (alreadyMember)
        {
            return ServiceResult.Fail("Kullanici zaten bu takimin uyesi.");
        }

        _unitOfWork.TeamMembers.Add(new TeamMember
        {
            TeamId = teamId,
            UserId = newMember.Id
        });

        await _unitOfWork.SaveChangesAsync();
        await IndexPersonDocumentAsync(newMember.Id);

        var actor = await _unitOfWork.Users.GetByIdAsync(userId);
        var actorName = actor is null ? string.Empty : UserDisplayNameHelper.Format(actor);
        await _notificationDispatch.NotifyTeamMemberAddedAsync(
            newMember.Id,
            userId,
            teamId,
            team.Name,
            actorName);

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RemoveMemberAsync(int teamId, int memberUserId, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (team.LeaderUserId != userId)
        {
            return ServiceResult.Fail("Sadece takim lideri uye cikarabilir.", ServiceErrorKind.Forbidden);
        }

        if (memberUserId == team.LeaderUserId)
        {
            return ServiceResult.Fail("Takim lideri takimdan cikarilamaz.");
        }

        var membership = (await _unitOfWork.TeamMembers.GetAllAsync())
            .FirstOrDefault(member => member.TeamId == teamId && member.UserId == memberUserId);

        if (membership is null)
        {
            return ServiceResult.Fail("Kullanici bu takimin uyesi degil.");
        }

        await _unitOfWork.TeamMembers.DeleteAsync(membership.Id);
        await _unitOfWork.SaveChangesAsync();
        await IndexPersonDocumentAsync(memberUserId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<IEnumerable<TaskActivityLogDto>>> GetTeamActivityAsync(int teamId, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult<IEnumerable<TaskActivityLogDto>>.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await IsTeamMemberAsync(teamId, userId))
        {
            return ServiceResult<IEnumerable<TaskActivityLogDto>>.Fail("Bu takimin uyesi degilsiniz.", ServiceErrorKind.Forbidden);
        }

        var users = (await _unitOfWork.Users.GetAllAsync()).ToList();
        var displayNameByEmail = UserDisplayNameHelper.BuildDisplayNameByEmail(users);
        var userEmails = users.ToDictionary(user => user.Id, user => user.Email);
        var logs = (await _unitOfWork.TaskActivityLogs.GetAllAsync())
            .Where(log => log.TeamId == teamId)
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

    public async Task<bool> IsTeamMemberAsync(int teamId, int userId)
    {
        return (await _unitOfWork.TeamMembers.GetAllAsync())
            .Any(member => member.TeamId == teamId && member.UserId == userId);
    }

    public async Task<IReadOnlyList<int>> GetTeamIdsForUserAsync(int userId)
    {
        return (await _unitOfWork.TeamMembers.GetAllAsync())
            .Where(member => member.UserId == userId)
            .Select(member => member.TeamId)
            .Distinct()
            .ToList();
    }

    public async Task<int> EnsurePersonalTeamAsync(int userId)
    {
        var existingTeam = (await _unitOfWork.Teams.GetAllAsync())
            .FirstOrDefault(team => team.IsPersonal && team.LeaderUserId == userId);

        if (existingTeam is not null)
        {
            return existingTeam.Id;
        }

        var team = new Team
        {
            Name = "Kisisel Gorevlerim",
            LeaderUserId = userId,
            CreatedByUserId = userId,
            IsPersonal = true
        };

        _unitOfWork.Teams.Add(team);
        await _unitOfWork.SaveChangesAsync();

        _unitOfWork.TeamMembers.Add(new TeamMember
        {
            TeamId = team.Id,
            UserId = userId
        });

        var board = new Board
        {
            TeamId = team.Id,
            Name = DefaultBoardName,
            DisplayOrder = 0
        };
        _unitOfWork.Boards.Add(board);
        await _unitOfWork.SaveChangesAsync();

        _unitOfWork.TeamBoardColumns.Add(new TeamBoardColumn
        {
            BoardId = board.Id,
            Title = "Yapilacaklar",
            DisplayOrder = 0,
            IsCompletedColumn = false
        });

        _unitOfWork.TeamBoardColumns.Add(new TeamBoardColumn
        {
            BoardId = board.Id,
            Title = "Tamamlandi",
            DisplayOrder = 1,
            IsCompletedColumn = true
        });

        await _unitOfWork.SaveChangesAsync();
        return team.Id;
    }

    private async Task<ServiceResult<TeamBoardDto>> BuildBoardDtoAsync(TeamDetailDto team, Board board)
    {
        var columns = (await _unitOfWork.TeamBoardColumns.GetAllAsync())
            .Where(column => column.BoardId == board.Id)
            .OrderBy(column => column.DisplayOrder)
            .ToList();

        var tasks = (await _unitOfWork.TaskItems.GetAllAsync())
            .Where(task => task.BoardId == board.Id)
            .ToList();

        var subtasksByParent = tasks
            .Where(task => task.ParentTaskId.HasValue)
            .GroupBy(task => task.ParentTaskId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var rootTasks = tasks
            .Where(task => task.ParentTaskId == null && task.SprintId.HasValue)
            .ToList();

        var categoryMap = (await _unitOfWork.Categories.GetAllAsync())
            .ToDictionary(category => category.Id, category => category.Name);
        var userMap = (await _unitOfWork.Users.GetAllAsync())
            .ToDictionary(user => user.Id, user => user.Email);
        var columnMap = columns.ToDictionary(column => column.Id, column => column.Title);

        var teamEntity = await _unitOfWork.Teams.GetByIdAsync(team.Id);

        var taskDtos = rootTasks.Select(task =>
        {
            var children = subtasksByParent.GetValueOrDefault(task.Id) ?? [];
            var doneCount = children.Count(child => child.SubtaskStatus == SubtaskStatus.Done);
            return new TaskListDto
            {
                Id = task.Id,
                Title = task.Title,
                Description = task.Description,
                CategoryId = task.CategoryId,
                CategoryName = task.CategoryId.HasValue && categoryMap.TryGetValue(task.CategoryId.Value, out var categoryName)
                    ? categoryName
                    : null,
                Priority = task.Priority,
                StartDate = task.StartDate,
                DueDate = task.DueDate,
                CompletedAt = task.CompletedAt,
                IsCompleted = task.IsCompleted,
                TeamId = task.TeamId,
                BoardId = task.BoardId,
                BoardName = board.Name,
                BoardColumnId = task.BoardColumnId,
                DisplayOrder = task.DisplayOrder,
                BoardColumnTitle = columnMap.GetValueOrDefault(task.BoardColumnId),
                AssignedToUserId = task.AssignedToUserId,
                AssignedToEmail = task.AssignedToUserId.HasValue
                    ? userMap.GetValueOrDefault(task.AssignedToUserId.Value)
                    : null,
                AssignmentStatus = task.AssignmentStatus,
                ParentTaskId = null,
                SubtaskStatus = null,
                SubtaskDoneCount = doneCount,
                SubtaskTotal = children.Count,
                SprintId = task.SprintId,
                SprintOrder = task.SprintOrder,
                TeamName = team.Name,
                IsPersonalTeam = teamEntity?.IsPersonal ?? false
            };
        })
            .OrderBy(task => task.DisplayOrder)
            .ThenBy(task => task.Id)
            .ToList();

        var activeSprint = (await _unitOfWork.Sprints.GetAllAsync())
            .FirstOrDefault(sprint => sprint.BoardId == board.Id && sprint.Status == SprintStatus.Active);

        return ServiceResult<TeamBoardDto>.Ok(new TeamBoardDto
        {
            TeamId = team.Id,
            TeamName = team.Name,
            BoardId = board.Id,
            BoardName = board.Name,
            ActiveSprintId = activeSprint?.Id,
            ActiveSprintName = activeSprint?.Name,
            Columns = columns.Select(column => new TeamBoardColumnWithTasksDto
            {
                Id = column.Id,
                Title = column.Title,
                DisplayOrder = column.DisplayOrder,
                IsCompletedColumn = column.IsCompletedColumn,
                Tasks = taskDtos.Where(task => task.BoardColumnId == column.Id).ToList()
            }).ToList()
        });
    }

    private async Task<ServiceResult> EnsureBoardAccessAsync(int teamId, int boardId, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await IsTeamMemberAsync(teamId, userId))
        {
            return ServiceResult.Fail("Bu takimin uyesi degilsiniz.", ServiceErrorKind.Forbidden);
        }

        var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
        if (board is null || board.TeamId != teamId)
        {
            return ServiceResult.Fail("Pano bulunamadi.", ServiceErrorKind.NotFound);
        }

        return ServiceResult.Ok();
    }

    private static BoardListDto MapBoardListDto(Board board)
    {
        return new BoardListDto
        {
            Id = board.Id,
            TeamId = board.TeamId,
            Name = board.Name,
            DisplayOrder = board.DisplayOrder,
            CreatedDate = board.CreatedDate
        };
    }

    private static List<string> ResolveColumnTitles(IReadOnlyList<string>? columnTitles)
    {
        if (!HasCustomTitles(columnTitles))
        {
            return DefaultColumnTitles.ToList();
        }

        return columnTitles!
            .Select(title => title.Trim())
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Take(20)
            .ToList();
    }

    private static bool HasCustomTitles(IReadOnlyList<string>? columnTitles)
    {
        return columnTitles is not null && columnTitles.Any(title => !string.IsNullOrWhiteSpace(title));
    }

    private async Task IndexPersonDocumentAsync(int userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user is null)
        {
            return;
        }

        var membershipTeamIds = (await _unitOfWork.TeamMembers.GetAllAsync())
            .Where(member => member.UserId == userId)
            .Select(member => member.TeamId)
            .ToHashSet();

        var nonPersonalTeamIds = (await _unitOfWork.Teams.GetAllAsync())
            .Where(team => membershipTeamIds.Contains(team.Id) && !team.IsPersonal)
            .Select(team => team.Id)
            .ToList();

        _searchIndex.IndexPerson(user, nonPersonalTeamIds);
    }

    private async Task ReindexTasksInColumnAsync(TeamBoardColumn column)
    {
        var board = await _unitOfWork.Boards.GetByIdAsync(column.BoardId);
        if (board is null)
        {
            return;
        }

        var team = await _unitOfWork.Teams.GetByIdAsync(board.TeamId);
        if (team is null || team.IsPersonal)
        {
            return;
        }

        var tasks = (await _unitOfWork.TaskItems.GetAllAsync())
            .Where(task => task.BoardColumnId == column.Id);

        foreach (var task in tasks)
        {
            _searchIndex.IndexTask(task, team.Name, column.Title);
        }
    }
}
