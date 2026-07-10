using System.Text.Json.Serialization;

namespace Todoo.Entities.Entities;

public class TaskAttachment
{
    public int Id { get; set; }

    public int TaskId { get; set; }

    public int UploadedByUserId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string ObjectKey { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public TaskItem Task { get; set; } = null!;

    [JsonIgnore]
    public User UploadedBy { get; set; } = null!;
}
