using Todoo.Business.Abstract;
using Todoo.DataAccess.UnitOfWork;
using Todoo.Entities.Entities;

namespace Todoo.Business.Concrete;

public class CategoryService : ICategoryService
{
    private const string OtherCategoryName = "Diğer";

    private static readonly string[] DefaultCategories =
    [
        "İş",
        "Okul",
        "Ev",
        "Sağlık",
        "Finans",
        OtherCategoryName
    ];

    private readonly IUnitOfWork _unitOfWork;

    public CategoryService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IEnumerable<Category>> GetAllCategoriesAsync()
    {
        var categories = await _unitOfWork.Categories.GetAllAsync();
        return categories
            .OrderBy(category => category.IsDefault)
            .ThenBy(category => category.Name);
    }

    public async Task<Category?> CreateCategoryAsync(Category category)
    {
        var normalizedName = category.Name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        var allCategories = await _unitOfWork.Categories.GetAllAsync();
        var exists = allCategories.Any(existingCategory =>
            existingCategory.Name.ToLower() == normalizedName.ToLower());

        if (exists)
        {
            return null;
        }

        category.Name = normalizedName;
        category.IsDefault = false;

        _unitOfWork.Categories.Add(category);
        await _unitOfWork.SaveChangesAsync();
        return category;
    }

    public async Task<Category?> UpdateCategoryAsync(int id, string name)
    {
        var category = await _unitOfWork.Categories.GetByIdAsync(id);
        if (category is null || category.IsDefault)
        {
            return null;
        }

        var normalizedName = name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        var allCategories = await _unitOfWork.Categories.GetAllAsync();
        var exists = allCategories.Any(existingCategory =>
            existingCategory.Id != id &&
            existingCategory.Name.ToLower() == normalizedName.ToLower());

        if (exists)
        {
            return null;
        }

        category.Name = normalizedName;
        _unitOfWork.Categories.Update(category);
        await _unitOfWork.SaveChangesAsync();
        return category;
    }

    public async Task<bool> DeleteCategoryAsync(int id)
    {
        var category = await _unitOfWork.Categories.GetByIdAsync(id);
        if (category is null || category.IsDefault) //default kategoriler silinemez
        {
            return false;
        }

        var tasks = await _unitOfWork.TaskItems.GetAllAsync();
        var isUsedByAnyTask = tasks.Any(task => task.CategoryId == id); //kategorinin altında görev varsa silinemez

        if (isUsedByAnyTask)
        {
            return false;
        }

        await _unitOfWork.Categories.DeleteAsync(id);
        await _unitOfWork.SaveChangesAsync(); //unit of work pattern kullanıldığı için değişiklikleri kaydetmek gerekiyor
        return true;
    }

    public async Task EnsureDefaultCategoriesAsync()
    {
        var allCategories = await _unitOfWork.Categories.GetAllAsync();
        var addedAny = false;
        var updatedAny = false;

        foreach (var categoryName in DefaultCategories)
        {
            var existing = allCategories.FirstOrDefault(category =>
                category.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase)); //default kategoriler zaten varsa güncellenmeyecek

            if (existing is null) // yoksa eklenebilir
            {
                _unitOfWork.Categories.Add(new Category
                {
                    Name = categoryName,
                    IsDefault = true
                });
                addedAny = true;
                continue;
            }

            if (!existing.IsDefault)
            {
                existing.IsDefault = true;
                _unitOfWork.Categories.Update(existing);
                updatedAny = true;
            }
        }

        if (addedAny || updatedAny)
        {
            await _unitOfWork.SaveChangesAsync();
        }
    }

    public async Task<int?> GetOtherCategoryIdAsync()
    {
        var categories = await _unitOfWork.Categories.GetAllAsync();
        return categories
            .FirstOrDefault(category =>
                category.Name.Equals(OtherCategoryName, StringComparison.OrdinalIgnoreCase))?.Id;
    }
}
