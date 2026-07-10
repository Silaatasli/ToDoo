using System.Text.Json.Serialization;

namespace Todoo.Entities.Entities;

public class CommentAttachment
{
    public int Id { get; set; }

    public int CommentId { get; set; }

    public int UploadedByUserId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string ObjectKey { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public TaskComment Comment { get; set; } = null!;

    [JsonIgnore]
    public User UploadedBy { get; set; } = null!;
}
