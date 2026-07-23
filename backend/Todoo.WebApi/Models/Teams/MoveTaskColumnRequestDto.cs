using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Teams;

public class MoveTaskColumnRequestDto
{
    [Required(ErrorMessage = "BoardColumnId zorunludur.")]
    public int BoardColumnId { get; set; }

    /// <summary>
    /// Hedef sutundaki yeni indeks (0 tabanli). Null ise sutunun sonuna eklenir.
    /// Ayni sutun icinde yeniden siralama icin de kullanilir.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int? TargetIndex { get; set; }

    /// <summary>
    /// Tamamlandi sutununa tasirken eksik alt gorevleri de Done yapar.
    /// </summary>
    public bool CompleteRemainingSubtasks { get; set; }
}
