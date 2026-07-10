using Todoo.Entities.Entities;

namespace Todoo.Business.Abstract;

public interface ICategoryService
{
    Task<IEnumerable<Category>> GetAllCategoriesAsync();
    Task<Category?> CreateCategoryAsync(Category category);
    Task<Category?> UpdateCategoryAsync(int id, string name);
    Task<bool> DeleteCategoryAsync(int id);
    Task EnsureDefaultCategoriesAsync();
    Task<int?> GetOtherCategoryIdAsync();
}
