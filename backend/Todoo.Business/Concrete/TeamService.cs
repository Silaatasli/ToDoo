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
    private static readonly string[] DefaultColumnTitles = ["All Tasks", "In Progress", "Completed"];

    private readonly IUnitOfWork _unitOfWork;
    private readonly ITeamBoardNotifier _boardNotifier;

    public TeamService(IUnitOfWork unitOfWork, ITeamBoardNotifier boardNotifier)
    {
        _unitOfWork = unitOfWork;
        _boardNotifier = boardNotifier;
    }

    public async Task<ServiceResult<TeamDetailDto>> CreateTeamAsync(string name, IReadOnlyList<string>? columnTitles, int userId)
    {
        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return ServiceResult<TeamDetailDto>.Fail("Takim adi bos olamaz.");
        }

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

        for (var i = 0; i < titles.Count; i++)
        {
            var isCompletedColumn = titles[i].Contains("complet", StringComparison.OrdinalIgnoreCase)
                || titles[i].Contains("tamam", StringComparison.OrdinalIgnoreCase)
                || (!HasCustomTitles(columnTitles) && titles.Count == 3 && i == 2);

            _unitOfWork.TeamBoardColumns.Add(new TeamBoardColumn
            {
                TeamId = team.Id,
                Title = titles[i],
                DisplayOrder = i,
                IsCompletedColumn = isCompletedColumn
            });
        }

        await _unitOfWork.SaveChangesAsync();
        return await GetTeamByIdAsync(team.Id, userId);
    }

    public async Task<IEnumerable<TeamListDto>> GetTeamsForUserAsync(int userId)
    {
        var memberTeamIds = (await _unitOfWork.TeamMembers.GetAllAsync()) // user için tüm membershipleri al
            .Where(member => member.UserId == userId) // userId ile eşleşenleri filtrele
            .Select(member => member.TeamId) // sadece teamId'leri al
            .ToHashSet(); // hızlı arama için hashset

        var teams = (await _unitOfWork.Teams.GetAllAsync())
            .Where(team => memberTeamIds.Contains(team.Id) && !team.IsPersonal) // user'ın üyesi olduğu ve kişisel olmayan takımları filtrele
            .OrderByDescending(team => team.CreatedDate); // en son oluşturulan takımlar önce gelsin

        var users = (await _unitOfWork.Users.GetAllAsync()).ToDictionary(user => user.Id, user => user.Email); // userId -> email map'i oluştur
        var memberCounts = (await _unitOfWork.TeamMembers.GetAllAsync()) // tüm team üyelerini al
            .GroupBy(member => member.TeamId) // teamId'ye göre grupla
            .ToDictionary(group => group.Key, group => group.Count()); //üye sayılarını tut

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
                    JoinedDate = member.JoinedDate,
                    HasProfilePhoto = !string.IsNullOrWhiteSpace(user?.ProfilePhotoObjectKey)
                };
            })
            .ToList();

        var columns = (await _unitOfWork.TeamBoardColumns.GetAllAsync())
            .Where(column => column.TeamId == teamId)
            .OrderBy(column => column.DisplayOrder)
            .Select(column => new TeamBoardColumnDto
            {
                Id = column.Id,
                Title = column.Title,
                DisplayOrder = column.DisplayOrder,
                IsCompletedColumn = column.IsCompletedColumn
            })
            .ToList();

        return ServiceResult<TeamDetailDto>.Ok(new TeamDetailDto
        {
            Id = team.Id,
            Name = team.Name,
            LeaderUserId = team.LeaderUserId,
            LeaderEmail = users.GetValueOrDefault(team.LeaderUserId)?.Email ?? string.Empty,
            CreatedDate = team.CreatedDate,
            Members = members,
            BoardColumns = columns
        });
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

        var columns = (await _unitOfWork.TeamBoardColumns.GetAllAsync())
            .Where(column => column.TeamId == teamId)
            .OrderBy(column => column.DisplayOrder)
            .ToList();

        var tasks = (await _unitOfWork.TaskItems.GetAllAsync())
            .Where(task => task.TeamId == teamId)
            .ToList();

        var categoryMap = (await _unitOfWork.Categories.GetAllAsync())
            .ToDictionary(category => category.Id, category => category.Name);
        var userMap = (await _unitOfWork.Users.GetAllAsync())
            .ToDictionary(user => user.Id, user => user.Email);
        var columnMap = columns.ToDictionary(column => column.Id, column => column.Title);

        var teamEntity = await _unitOfWork.Teams.GetByIdAsync(teamId);

        var taskDtos = tasks.Select(task => new TaskListDto
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
            IsCompleted = task.IsCompleted,
            TeamId = task.TeamId,
            BoardColumnId = task.BoardColumnId,
            BoardColumnTitle = columnMap.GetValueOrDefault(task.BoardColumnId),
            AssignedToUserId = task.AssignedToUserId,
            AssignedToEmail = task.AssignedToUserId.HasValue
                ? userMap.GetValueOrDefault(task.AssignedToUserId.Value)
                : null,
            AssignmentStatus = task.AssignmentStatus,
            TeamName = teamResult.Data!.Name,
            IsPersonalTeam = teamEntity?.IsPersonal ?? false
        }).ToList();

        return ServiceResult<TeamBoardDto>.Ok(new TeamBoardDto
        {
            TeamId = teamId,
            TeamName = teamResult.Data!.Name,
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

    public async Task<ServiceResult<TeamBoardColumnDto>> AddBoardColumnAsync(int teamId, string title, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult<TeamBoardColumnDto>.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await IsTeamMemberAsync(teamId, userId))
        {
            return ServiceResult<TeamBoardColumnDto>.Fail("Bu takimin uyesi degilsiniz.", ServiceErrorKind.Forbidden);
        }

        var trimmedTitle = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            return ServiceResult<TeamBoardColumnDto>.Fail("Sutun basligi bos olamaz.");
        }

        var columns = (await _unitOfWork.TeamBoardColumns.GetAllAsync())
            .Where(column => column.TeamId == teamId)
            .ToList();

        var nextOrder = columns.Count == 0 ? 0 : columns.Max(column => column.DisplayOrder) + 1;
        var column = new TeamBoardColumn
        {
            TeamId = teamId,
            Title = trimmedTitle,
            DisplayOrder = nextOrder,
            IsCompletedColumn = trimmedTitle.Contains("complet", StringComparison.OrdinalIgnoreCase)
                || trimmedTitle.Contains("tamam", StringComparison.OrdinalIgnoreCase)
        };

        _unitOfWork.TeamBoardColumns.Add(column);
        await _unitOfWork.SaveChangesAsync();
        await _boardNotifier.NotifyBoardChangedAsync(teamId, TeamBoardChangeTypes.ColumnAdded, userId);

        return ServiceResult<TeamBoardColumnDto>.Ok(new TeamBoardColumnDto
        {
            Id = column.Id,
            Title = column.Title,
            DisplayOrder = column.DisplayOrder,
            IsCompletedColumn = column.IsCompletedColumn
        });
    }

    public async Task<ServiceResult<TeamBoardColumnDto>> UpdateBoardColumnAsync(int teamId, int columnId, string title, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult<TeamBoardColumnDto>.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await IsTeamMemberAsync(teamId, userId))
        {
            return ServiceResult<TeamBoardColumnDto>.Fail("Bu takimin uyesi degilsiniz.", ServiceErrorKind.Forbidden);
        }

        var trimmedTitle = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            return ServiceResult<TeamBoardColumnDto>.Fail("Sutun basligi bos olamaz.");
        }

        var column = await _unitOfWork.TeamBoardColumns.GetByIdAsync(columnId);
        if (column is null || column.TeamId != teamId)
        {
            return ServiceResult<TeamBoardColumnDto>.Fail("Sutun bulunamadi.", ServiceErrorKind.NotFound);
        }

        column.Title = trimmedTitle;
        column.IsCompletedColumn = trimmedTitle.Contains("complet", StringComparison.OrdinalIgnoreCase)
            || trimmedTitle.Contains("tamam", StringComparison.OrdinalIgnoreCase);

        _unitOfWork.TeamBoardColumns.Update(column);
        await _unitOfWork.SaveChangesAsync();
        await _boardNotifier.NotifyBoardChangedAsync(teamId, TeamBoardChangeTypes.ColumnUpdated, userId);

        return ServiceResult<TeamBoardColumnDto>.Ok(new TeamBoardColumnDto
        {
            Id = column.Id,
            Title = column.Title,
            DisplayOrder = column.DisplayOrder,
            IsCompletedColumn = column.IsCompletedColumn
        });
    }

    public async Task<ServiceResult> ReorderBoardColumnsAsync(int teamId, IReadOnlyList<int> orderedColumnIds, int userId)
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

        var columns = (await _unitOfWork.TeamBoardColumns.GetAllAsync())
            .Where(column => column.TeamId == teamId)
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
        await _boardNotifier.NotifyBoardChangedAsync(teamId, TeamBoardChangeTypes.ColumnsReordered, userId);
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

        await _unitOfWork.Teams.DeleteAsync(teamId);
        await _unitOfWork.SaveChangesAsync();
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

        _unitOfWork.TeamBoardColumns.Add(new TeamBoardColumn
        {
            TeamId = team.Id,
            Title = "Yapilacaklar",
            DisplayOrder = 0,
            IsCompletedColumn = false
        });

        _unitOfWork.TeamBoardColumns.Add(new TeamBoardColumn
        {
            TeamId = team.Id,
            Title = "Tamamlandi",
            DisplayOrder = 1,
            IsCompletedColumn = true
        });

        await _unitOfWork.SaveChangesAsync();
        return team.Id;
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
}
