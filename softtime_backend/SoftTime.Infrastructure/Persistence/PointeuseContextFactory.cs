using Microsoft.EntityFrameworkCore;

using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Persistence;

public sealed class PointeuseContextFactory : IPointeuseContextFactory
{
    public IDisposablePointeuseContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<PointeuseDbContext>()
            .UseSqlServer(connectionString, o => o.CommandTimeout(120))
            .Options;
        return new DisposablePointeuseContext(new PointeuseDbContext(options));
    }
}
