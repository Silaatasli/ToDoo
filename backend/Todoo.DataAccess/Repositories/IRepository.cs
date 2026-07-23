namespace Todoo.DataAccess.Repositories;

public interface IRepository<T> where T : class
{
    Task<List<T>> GetAllAsync();
    Task<List<T>> GetAllIgnoreFiltersAsync();
    Task<T?> GetByIdAsync(int id);
    Task<T?> GetByIdIgnoreFiltersAsync(int id);
    void Add(T entity);
    void Update(T entity);
    Task DeleteAsync(int id);
}
