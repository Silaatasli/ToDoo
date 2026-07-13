using Todoo.DataAccess.Contexts;
using Todoo.DataAccess.Repositories;
using Todoo.Entities.Entities;

namespace Todoo.DataAccess.UnitOfWork;

public class UnitOfWork : IUnitOfWork
{
    private readonly TodooDbContext _context;
    private IRepository<TaskItem>? _taskItems;
    private IRepository<User>? _users;
    private IRepository<Category>? _categories;
    private IRepository<Team>? _teams;
    private IRepository<TeamMember>? _teamMembers;
    private IRepository<Board>? _boards;
    private IRepository<TeamBoardColumn>? _teamBoardColumns;
    private IRepository<TaskActivityLog>? _taskActivityLogs;
    private IRepository<TaskAttachment>? _taskAttachments;
    private IRepository<TaskComment>? _taskComments;
    private IRepository<CommentAttachment>? _commentAttachments;

    public UnitOfWork(TodooDbContext context)
    {
        _context = context;
    }

    public IRepository<TaskItem> TaskItems => _taskItems ??= new Repository<TaskItem>(_context);
    public IRepository<User> Users => _users ??= new Repository<User>(_context);
    public IRepository<Category> Categories => _categories ??= new Repository<Category>(_context);
    public IRepository<Team> Teams => _teams ??= new Repository<Team>(_context);
    public IRepository<TeamMember> TeamMembers => _teamMembers ??= new Repository<TeamMember>(_context);
    public IRepository<Board> Boards => _boards ??= new Repository<Board>(_context);
    public IRepository<TeamBoardColumn> TeamBoardColumns => _teamBoardColumns ??= new Repository<TeamBoardColumn>(_context);
    public IRepository<TaskActivityLog> TaskActivityLogs => _taskActivityLogs ??= new Repository<TaskActivityLog>(_context);
    public IRepository<TaskAttachment> TaskAttachments => _taskAttachments ??= new Repository<TaskAttachment>(_context);
    public IRepository<TaskComment> TaskComments => _taskComments ??= new Repository<TaskComment>(_context);
    public IRepository<CommentAttachment> CommentAttachments => _commentAttachments ??= new Repository<CommentAttachment>(_context);

    public Task<int> SaveChangesAsync()
    {
        return _context.SaveChangesAsync();
    }
}
