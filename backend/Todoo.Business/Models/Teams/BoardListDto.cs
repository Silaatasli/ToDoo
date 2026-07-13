namespace Todoo.Business.Models.Teams;

public class BoardListDto
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public DateTime CreatedDate { get; set; }
}
