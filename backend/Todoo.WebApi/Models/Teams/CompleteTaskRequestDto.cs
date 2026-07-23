namespace Todoo.WebApi.Models.Teams;

public class CompleteTaskRequestDto
{
    /// <summary>
    /// Ana gorevi tamamlarken eksik alt gorevleri de Done yapar.
    /// </summary>
    public bool CompleteRemainingSubtasks { get; set; }
}
