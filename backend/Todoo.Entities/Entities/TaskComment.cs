using System.Text.Json.Serialization;

namespace Todoo.Entities.Entities;

public class TaskComment
{
    public int Id { get; set; }

    public int TaskId { get; set; }

    public int AuthorUserId { get; set; }

    public int? ParentCommentId { get; set; }

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public TaskItem Task { get; set; } = null!;

    [JsonIgnore]
    public User Author { get; set; } = null!;

    [JsonIgnore]
    public TaskComment? ParentComment { get; set; }

    [JsonIgnore]
    public ICollection<TaskComment> Replies { get; set; } = [];

    [JsonIgnore]
    public ICollection<CommentAttachment> Attachments { get; set; } = [];
}
