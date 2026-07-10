namespace Todoo.Business.Models;

public class TaskCommentDto
{
    public int Id { get; set; }

    public int TaskId { get; set; }

    public int? ParentCommentId { get; set; }

    public string Body { get; set; } = string.Empty;

    public int AuthorUserId { get; set; }

    public string AuthorEmail { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; }

    public IEnumerable<CommentAttachmentDto> Attachments { get; set; } = [];

    public IEnumerable<TaskCommentDto> Replies { get; set; } = [];
}
