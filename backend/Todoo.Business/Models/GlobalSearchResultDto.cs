namespace Todoo.Business.Models;

public class GlobalSearchResultDto
{
    public IEnumerable<GlobalSearchTeamDto> Teams { get; set; } = [];

    public IEnumerable<GlobalSearchBoardDto> Boards { get; set; } = [];

    public IEnumerable<GlobalSearchTaskDto> Tasks { get; set; } = [];

    public IEnumerable<GlobalSearchPersonDto> People { get; set; } = [];
}

public class GlobalSearchTeamDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public class GlobalSearchBoardDto // pano arama için
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int TeamId { get; set; }

    public string TeamName { get; set; } = string.Empty;
}

public class GlobalSearchTaskDto
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public int TeamId { get; set; }

    public string TeamName { get; set; } = string.Empty;

    public int BoardId { get; set; }

    public string BoardColumnTitle { get; set; } = string.Empty;
}

public class GlobalSearchPersonDto
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool HasProfilePhoto { get; set; }
}
