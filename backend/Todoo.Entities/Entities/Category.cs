using System.Text.Json.Serialization;

namespace Todoo.Entities.Entities;

public class Category
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    [JsonIgnore]
    public ICollection<TaskItem> TaskItems { get; set; } = [];
}
