namespace Todoo.Business.Models;

public class UserProfileDto
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Title { get; set; }

    public DateTime CreatedDate { get; set; }

    public bool IsSelf { get; set; }
}
