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

    public async Task<List<T>> GetAllIgnoreFiltersAsync()
    {
        return await _dbSet.IgnoreQueryFilters().ToListAsync();
    }

    public async Task<T?> GetByIdAsync(int id)
    {
        // FirstOrDefault applies global query filters (FindAsync does not).
        return await _dbSet.FirstOrDefaultAsync(entity => EF.Property<int>(entity, "Id") == id);
    }

    public async Task<T?> GetByIdIgnoreFiltersAsync(int id)
    {
        return await _dbSet
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entity => EF.Property<int>(entity, "Id") == id);
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
        var entity = await GetByIdIgnoreFiltersAsync(id);
        if (entity is null)
        {
            return;
        }

        _dbSet.Remove(entity);
    }
}
