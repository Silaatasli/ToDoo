using Todoo.Business.Models.Sprints;

namespace Todoo.Business.Abstract;

public interface ISprintAuditSearchService
{
    Task IndexAsync(SprintAuditWriteRequest entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SprintAuditEntryDto>> SearchBySprintAsync(
        int sprintId,
        int take = 100,
        CancellationToken cancellationToken = default);
}
