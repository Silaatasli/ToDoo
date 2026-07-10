using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Todoo.Business.Abstract;
using Todoo.Entities.Entities;
using Todoo.WebApi.Models.Categories;

namespace Todoo.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    public CategoriesController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var categories = await _categoryService.GetAllCategoriesAsync();
        return Ok(categories);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequestDto request)
    {
        var category = new Category
        {
            Name = request.Name
        };

        var createdCategory = await _categoryService.CreateCategoryAsync(category);
        if (createdCategory is null)
        {
            return BadRequest("Kategori adi bos olamaz veya ayni kategori zaten mevcut.");
        }

        return Ok(createdCategory);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCategoryRequestDto request)
    {
        var updatedCategory = await _categoryService.UpdateCategoryAsync(id, request.Name);
        if (updatedCategory is null)
        {
            return BadRequest("Varsayilan kategoriler duzenlenemez, kategori bulunamadi veya ayni isimde kategori mevcut.");
        }

        return Ok(updatedCategory);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _categoryService.DeleteCategoryAsync(id);
        if (!deleted)
        {
            return BadRequest("Varsayilan kategoriler silinemez, kategori bulunamadi veya bu kategoriye bagli gorevler oldugu icin silinemedi.");
        }

        return NoContent();
    }
}
