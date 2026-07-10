using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Categories;

public class UpdateCategoryRequestDto
{
    [Required(ErrorMessage = "Kategori adi zorunludur.")]
    [MaxLength(100, ErrorMessage = "Kategori adi en fazla 100 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;
}
