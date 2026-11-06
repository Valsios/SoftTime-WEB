using Microsoft.EntityFrameworkCore;

using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Persistence;

public sealed class SageContextFactory : ISageContextFactory
{
    public IDisposableSageContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SageDbContext>()
            .UseSqlServer(connectionString, o => o.CommandTimeout(120))
            .Options;
        return new DisposableSageContext(new SageDbContext(options));
    }
}
