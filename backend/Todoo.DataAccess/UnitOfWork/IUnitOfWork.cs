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
    IRepository<Board> Boards { get; }
    IRepository<TeamBoardColumn> TeamBoardColumns { get; }
    IRepository<TaskActivityLog> TaskActivityLogs { get; }
    IRepository<TaskAttachment> TaskAttachments { get; }
    IRepository<TaskComment> TaskComments { get; }
    IRepository<CommentAttachment> CommentAttachments { get; }
    Task<int> SaveChangesAsync();
}
