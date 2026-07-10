using Todoo.DataAccess.Repositories;
using Todoo.Entities.Entities;

namespace Todoo.DataAccess.UnitOfWork;

public interface IUnitOfWork
{
    IRepository<TaskItem> TaskItems { get; }
    IRepository<User> Users { get; }
    IRepository<Category> Categories { get; }
    IRepository<Team> Teams { get; }
    IRepository<TeamMember> TeamMembers { get; }
    IRepository<TeamBoardColumn> TeamBoardColumns { get; }
    IRepository<TaskActivityLog> TaskActivityLogs { get; }
    IRepository<TaskAttachment> TaskAttachments { get; }
    Task<int> SaveChangesAsync();
}
