using System.ComponentModel.DataAnnotations;
using Todoo.Entities.Enums;

namespace Todoo.WebApi.Models.Teams;

public class CreateSubtaskRequestDto
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    public int? AssignedToUserId { get; set; }
}

public class UpdateSubtaskStatusRequestDto
{
    [Required]
    public SubtaskStatus Status { get; set; }
}
