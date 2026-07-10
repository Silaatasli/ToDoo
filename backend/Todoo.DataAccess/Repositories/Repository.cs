using Microsoft.EntityFrameworkCore;
using Todoo.DataAccess.Contexts;

namespace Todoo.DataAccess.Repositories;

public class Repository<T> : IRepository<T> where T : class
{
    private readonly TodooDbContext _context;
    private readonly DbSet<T> _dbSet;

    public Repository(TodooDbContext context)
    {
        _context = context;
        _dbSet = _context.Set<T>();
    }

    public async Task<List<T>> GetAllAsync()
    {
        return await _dbSet.ToListAsync();
    }

    public async Task<T?> GetByIdAsync(int id)
    {
        return await _dbSet.FindAsync(id);
    }

    public void Add(T entity)
    {
        _dbSet.Add(entity);
    }

    public void Update(T entity)
    {
        _dbSet.Update(entity);
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await GetByIdAsync(id);
        if (entity is null)
        {
            return;
        }

        _dbSet.Remove(entity);
    }
}
