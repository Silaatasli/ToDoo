namespace Todoo.DataAccess.Repositories;

public interface IRepository<T> where T : class
{
    Task<List<T>> GetAllAsync();
    Task<T?> GetByIdAsync(int id);
    void Add(T entity);
    void Update(T entity);
    Task DeleteAsync(int id);
}
