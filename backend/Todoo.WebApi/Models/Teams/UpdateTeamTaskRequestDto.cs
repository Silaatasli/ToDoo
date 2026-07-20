using System.ComponentModel.DataAnnotations;
using Todoo.Entities.Enums;

namespace Todoo.WebApi.Models.Teams;

public class UpdateTeamTaskRequestDto : IValidatableObject
{
    [Required(ErrorMessage = "Baslik zorunludur.")]
    [MaxLength(200, ErrorMessage = "Baslik en fazla 200 karakter olabilir.")]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000, ErrorMessage = "Aciklama en fazla 4000 karakter olabilir.")]
    public string? Description { get; set; }

    public int? CategoryId { get; set; }

    [EnumDataType(typeof(Priority), ErrorMessage = "Gecersiz oncelik degeri.")]
    public Priority Priority { get; set; }

    [Required(ErrorMessage = "Baslangic tarihi zorunludur.")]
    public DateTime StartDate { get; set; }

    public DateTime? DueDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DueDate.HasValue && DueDate.Value.Date < StartDate.Date)
        {
            yield return new ValidationResult(
                "Bitis tarihi baslangic tarihinden once olamaz.",
                [nameof(DueDate)]);
        }
    }
}
