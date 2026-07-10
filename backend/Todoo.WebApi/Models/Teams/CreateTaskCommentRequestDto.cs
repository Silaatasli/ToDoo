using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Teams;

public class CreateTaskCommentRequestDto
{
    [MaxLength(4000)]
    public string Body { get; set; } = string.Empty;

    public int? ParentCommentId { get; set; }
}
