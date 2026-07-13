using Todoo.Business.Models;
using Todoo.Entities.Entities;

namespace Todoo.Business.Abstract;

public interface ILuceneSearchIndex
{
    void IndexTeam(Team team);

    void IndexTask(TaskItem task, string teamName, string boardColumnTitle);

    void IndexPerson(User user, IEnumerable<int> nonPersonalTeamIds);

    void RemoveTeam(int teamId);

    void RemoveTask(int taskId);

    void RemovePerson(int userId);

    void RemoveTasksForTeam(int teamId);

    GlobalSearchResultDto Search(string query, IReadOnlyCollection<int> visibleTeamIds, int maxResultsPerSection);

    Task RebuildAsync(
        IEnumerable<Team> teams,
        IEnumerable<(TaskItem Task, string TeamName, string BoardColumnTitle)> tasks,
        IEnumerable<(User User, IReadOnlyCollection<int> TeamIds)> people,
        CancellationToken cancellationToken = default);
}
