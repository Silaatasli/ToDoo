namespace Todoo.Business.Models;

public class TaskAttachmentDto
{
    public int Id { get; set; }

    public int TaskId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public int UploadedByUserId { get; set; }

    public string UploadedByEmail { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; }
}
