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
    public DbSet<TeamBoardColumn> TeamBoardColumns => Set<TeamBoardColumn>();
    public DbSet<TaskActivityLog> TaskActivityLogs => Set<TaskActivityLog>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();

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

        modelBuilder.Entity<TeamBoardColumn>()
            .Property(column => column.Title)
            .HasMaxLength(100)
            .IsRequired();

        modelBuilder.Entity<TeamBoardColumn>()
            .HasOne(column => column.Team)
            .WithMany(team => team.BoardColumns)
            .HasForeignKey(column => column.TeamId)
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

        modelBuilder.Entity<Category>()
            .Property(category => category.Name)
            .HasMaxLength(100)
            .IsRequired();

        modelBuilder.Entity<Category>()
            .HasIndex(category => category.Name)
            .IsUnique();
    }
}
