using System.Text.Json.Serialization;

namespace Todoo.Entities.Entities;

public class User
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Title { get; set; }

    public byte[] PasswordHash { get; set; } = [];

    public byte[] PasswordSalt { get; set; } = [];

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public ICollection<TeamMember> TeamMemberships { get; set; } = [];

    [JsonIgnore]
    public ICollection<TaskItem> CreatedTasks { get; set; } = [];

    [JsonIgnore]
    public ICollection<TaskItem> AssignedTasks { get; set; } = [];
}
