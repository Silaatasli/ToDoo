using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Todoo.DataAccess.Contexts;

public class TodooDbContextFactory : IDesignTimeDbContextFactory<TodooDbContext>
{
    public TodooDbContext CreateDbContext(string[] args)
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Todoo.WebApi");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection bulunamadi.");

        var optionsBuilder = new DbContextOptionsBuilder<TodooDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new TodooDbContext(optionsBuilder.Options);
    }
}
