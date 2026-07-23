using Microsoft.EntityFrameworkCore;
using Todoo.Entities.Entities;

namespace Todoo.DataAccess.Contexts;

public class TodooDbContext : DbContext
{
    public TodooDbContext(DbContextOptions<TodooDbContext> options) : base(options)
    {
    }

    public DbSet<TaskItem> TaskItems => Set<TaskItem>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<TeamBoardColumn> TeamBoardColumns => Set<TeamBoardColumn>();
    public DbSet<TaskActivityLog> TaskActivityLogs => Set<TaskActivityLog>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<CommentAttachment> CommentAttachments => Set<CommentAttachment>();
    public DbSet<TeamAnnouncement> TeamAnnouncements => Set<TeamAnnouncement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasIndex(user => user.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .Property(user => user.Email)
            .HasMaxLength(256)
            .IsRequired();

        modelBuilder.Entity<User>()
            .Property(user => user.FirstName)
            .HasMaxLength(100);

        modelBuilder.Entity<User>()
            .Property(user => user.LastName)
            .HasMaxLength(100);

        modelBuilder.Entity<User>()
            .Property(user => user.Title)
            .HasMaxLength(100);

        modelBuilder.Entity<User>()
            .Property(user => user.PhoneNumber)
            .HasMaxLength(30);

        modelBuilder.Entity<User>()
            .Property(user => user.ProfilePhotoObjectKey)
            .HasMaxLength(500);

        modelBuilder.Entity<User>()
            .Property(user => user.ProfilePhotoContentType)
            .HasMaxLength(100);

        modelBuilder.Entity<User>()
            .Property(user => user.ProfilePhotoFileName)
            .HasMaxLength(255);

        modelBuilder.Entity<Team>()
            .Property(team => team.Name)
            .HasMaxLength(200)
            .IsRequired();

        modelBuilder.Entity<Team>()
            .HasOne(team => team.Leader)
            .WithMany()
            .HasForeignKey(team => team.LeaderUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Team>()
            .HasOne(team => team.CreatedBy)
            .WithMany()
            .HasForeignKey(team => team.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TeamMember>()
            .HasIndex(member => new { member.TeamId, member.UserId })
            .IsUnique();

        modelBuilder.Entity<TeamMember>()
            .HasOne(member => member.Team)
            .WithMany(team => team.Members)
            .HasForeignKey(member => member.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TeamMember>()
            .HasOne(member => member.User)
            .WithMany(user => user.TeamMemberships)
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Board>()
            .Property(board => board.Name)
            .HasMaxLength(200)
            .IsRequired();

        modelBuilder.Entity<Board>()
            .HasOne(board => board.Team)
            .WithMany(team => team.Boards)
            .HasForeignKey(board => board.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TeamBoardColumn>()
            .Property(column => column.Title)
            .HasMaxLength(100)
            .IsRequired();

        modelBuilder.Entity<TeamBoardColumn>()
            .HasOne(column => column.Board)
            .WithMany(board => board.Columns)
            .HasForeignKey(column => column.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TaskItem>()
            .Property(task => task.Title)
            .HasMaxLength(200)
            .IsRequired();

        modelBuilder.Entity<TaskItem>()
            .HasOne(task => task.Team)
            .WithMany(team => team.Tasks)
            .HasForeignKey(task => task.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TaskItem>()
            .HasOne(task => task.Board)
            .WithMany(board => board.Tasks)
            .HasForeignKey(task => task.BoardId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TaskItem>()
            .HasOne(task => task.BoardColumn)
            .WithMany(column => column.Tasks)
            .HasForeignKey(task => task.BoardColumnId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<TaskItem>()
            .HasOne(task => task.CreatedBy)
            .WithMany(user => user.CreatedTasks)
            .HasForeignKey(task => task.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TaskItem>()
            .HasOne(task => task.AssignedTo)
            .WithMany(user => user.AssignedTasks)
            .HasForeignKey(task => task.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TaskItem>()
            .HasOne(task => task.Category)
            .WithMany(category => category.TaskItems)
            .HasForeignKey(task => task.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TaskItem>()
            .HasOne(task => task.DeletedBy)
            .WithMany()
            .HasForeignKey(task => task.DeletedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TaskItem>()
            .HasOne(task => task.ParentTask)
            .WithMany(task => task.Subtasks)
            .HasForeignKey(task => task.ParentTaskId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TaskItem>()
            .HasIndex(task => task.ParentTaskId);

        modelBuilder.Entity<TaskItem>()
            .HasQueryFilter(task => task.DeletedAt == null);

        modelBuilder.Entity<TaskActivityLog>()
            .Property(log => log.OldValue)
            .HasMaxLength(500);

        modelBuilder.Entity<TaskActivityLog>()
            .Property(log => log.NewValue)
            .HasMaxLength(500);

        modelBuilder.Entity<TaskActivityLog>()
            .HasOne(log => log.Team)
            .WithMany(team => team.ActivityLogs)
            .HasForeignKey(log => log.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TaskActivityLog>()
            .HasOne(log => log.Task)
            .WithMany(task => task.ActivityLogs)
            .HasForeignKey(log => log.TaskId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<TaskActivityLog>()
            .HasOne(log => log.User)
            .WithMany()
            .HasForeignKey(log => log.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TaskAttachment>()
            .Property(attachment => attachment.FileName)
            .HasMaxLength(255)
            .IsRequired();

        modelBuilder.Entity<TaskAttachment>()
            .Property(attachment => attachment.ContentType)
            .HasMaxLength(120)
            .IsRequired();

        modelBuilder.Entity<TaskAttachment>()
            .Property(attachment => attachment.ObjectKey)
            .HasMaxLength(500)
            .IsRequired();

        modelBuilder.Entity<TaskAttachment>()
            .HasOne(attachment => attachment.Task)
            .WithMany(task => task.Attachments)
            .HasForeignKey(attachment => attachment.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TaskAttachment>()
            .HasOne(attachment => attachment.UploadedBy)
            .WithMany()
            .HasForeignKey(attachment => attachment.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TaskComment>()
            .Property(comment => comment.Body)
            .HasMaxLength(4000)
            .IsRequired();

        modelBuilder.Entity<TaskComment>()
            .HasOne(comment => comment.Task)
            .WithMany(task => task.Comments)
            .HasForeignKey(comment => comment.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TaskComment>()
            .HasOne(comment => comment.Author)
            .WithMany()
            .HasForeignKey(comment => comment.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TaskComment>()
            .HasOne(comment => comment.ParentComment)
            .WithMany(comment => comment.Replies)
            .HasForeignKey(comment => comment.ParentCommentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CommentAttachment>()
            .Property(attachment => attachment.FileName)
            .HasMaxLength(255)
            .IsRequired();

        modelBuilder.Entity<CommentAttachment>()
            .Property(attachment => attachment.ContentType)
            .HasMaxLength(120)
            .IsRequired();

        modelBuilder.Entity<CommentAttachment>()
            .Property(attachment => attachment.ObjectKey)
            .HasMaxLength(500)
            .IsRequired();

        modelBuilder.Entity<CommentAttachment>()
            .HasOne(attachment => attachment.Comment)
            .WithMany(comment => comment.Attachments)
            .HasForeignKey(attachment => attachment.CommentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CommentAttachment>()
            .HasOne(attachment => attachment.UploadedBy)
            .WithMany()
            .HasForeignKey(attachment => attachment.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TeamAnnouncement>()
            .Property(announcement => announcement.Title)
            .HasMaxLength(200)
            .IsRequired();

        modelBuilder.Entity<TeamAnnouncement>()
            .Property(announcement => announcement.Body)
            .HasMaxLength(4000)
            .IsRequired();

        modelBuilder.Entity<TeamAnnouncement>()
            .HasOne(announcement => announcement.Team)
            .WithMany(team => team.Announcements)
            .HasForeignKey(announcement => announcement.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TeamAnnouncement>()
            .HasOne(announcement => announcement.Author)
            .WithMany()
            .HasForeignKey(announcement => announcement.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TeamAnnouncement>()
            .HasIndex(announcement => new { announcement.TeamId, announcement.CreatedDate });

        modelBuilder.Entity<Category>()
            .Property(category => category.Name)
            .HasMaxLength(100)
            .IsRequired();

        modelBuilder.Entity<Category>()
            .HasIndex(category => category.Name)
            .IsUnique();
    }
}
