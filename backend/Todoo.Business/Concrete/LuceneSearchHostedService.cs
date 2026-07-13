using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Todoo.Business.Abstract;
using Todoo.DataAccess.UnitOfWork;

namespace Todoo.Business.Concrete;

public sealed class LuceneSearchHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILuceneSearchIndex _searchIndex;
    private readonly ILogger<LuceneSearchHostedService> _logger;

    public LuceneSearchHostedService(
        IServiceScopeFactory scopeFactory,
        ILuceneSearchIndex searchIndex,
        ILogger<LuceneSearchHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _searchIndex = searchIndex;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var teams = (await unitOfWork.Teams.GetAllAsync()).ToList();
            var nonPersonalTeams = teams.Where(team => !team.IsPersonal).ToList();
            var nonPersonalTeamIds = nonPersonalTeams.Select(team => team.Id).ToHashSet();
            var teamNameById = nonPersonalTeams.ToDictionary(team => team.Id, team => team.Name);

            var boards = (await unitOfWork.Boards.GetAllAsync())
                .Where(board => nonPersonalTeamIds.Contains(board.TeamId))
                .Select(board => (
                    Board: board,
                    TeamName: teamNameById.GetValueOrDefault(board.TeamId, string.Empty)))
                .ToList();

            var nonPersonalBoardIds = boards.Select(item => item.Board.Id).ToHashSet();

            var columns = (await unitOfWork.TeamBoardColumns.GetAllAsync())
                .Where(column => nonPersonalBoardIds.Contains(column.BoardId))
                .ToDictionary(column => column.Id, column => column.Title);

            var tasks = (await unitOfWork.TaskItems.GetAllAsync())
                .Where(task => nonPersonalTeamIds.Contains(task.TeamId))
                .Select(task => (
                    Task: task,
                    TeamName: teamNameById.GetValueOrDefault(task.TeamId, string.Empty),
                    BoardColumnTitle: columns.GetValueOrDefault(task.BoardColumnId, string.Empty)))
                .ToList();

            var memberships = (await unitOfWork.TeamMembers.GetAllAsync())
                .Where(member => nonPersonalTeamIds.Contains(member.TeamId))
                .ToList();

            var teamIdsByUserId = memberships
                .GroupBy(member => member.UserId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyCollection<int>)group.Select(member => member.TeamId).Distinct().ToList());

            var people = (await unitOfWork.Users.GetAllAsync())
                .Where(user => teamIdsByUserId.ContainsKey(user.Id))
                .Select(user => (User: user, TeamIds: teamIdsByUserId[user.Id]))
                .ToList();

            await _searchIndex.RebuildAsync(nonPersonalTeams, boards, tasks, people, cancellationToken);
            _logger.LogInformation(
                "Lucene arama indeksi yenilendi. Teams={TeamCount}, Boards={BoardCount}, Tasks={TaskCount}, People={PeopleCount}",
                nonPersonalTeams.Count,
                boards.Count,
                tasks.Count,
                people.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lucene arama indeksi yenilenirken hata olustu.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
