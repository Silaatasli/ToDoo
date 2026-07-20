using System.Text.Json.Serialization;

namespace Todoo.Entities.Entities;

public class TeamMember
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    public int UserId { get; set; }

    public DateTime JoinedDate { get; set; } = DateTime.UtcNow;

    /// <summary>Lider disinda duyuru yayinlayabilme yetkisi.</summary>
    public bool CanPublishAnnouncements { get; set; }

    [JsonIgnore]
    public Team Team { get; set; } = null!;

    [JsonIgnore]
    public User User { get; set; } = null!;
}
