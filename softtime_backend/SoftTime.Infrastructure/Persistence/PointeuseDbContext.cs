using Microsoft.EntityFrameworkCore;
using SoftTime.Domain.Entities.Pointeuse;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Persistence;

public class PointeuseDbContext : DbContext
{
    public PointeuseDbContext(DbContextOptions<PointeuseDbContext> options) : base(options) { }

    public DbSet<CHECKINOUT> CHECKINOUT => Set<CHECKINOUT>();
    public DbSet<USERINFO> USERINFO => Set<USERINFO>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CHECKINOUT>().HasKey(e => new { e.USERID, e.CHECKTIME });
    }
}

public sealed class DisposablePointeuseContext : IDisposablePointeuseContext
{
    private readonly PointeuseDbContext _db;
    public DisposablePointeuseContext(PointeuseDbContext db) => _db = db;
    public IQueryable<CHECKINOUT> CheckInOut => _db.CHECKINOUT;
    public IQueryable<USERINFO> Users => _db.USERINFO;
    public PointeuseDbContext Db => _db;
    public void Dispose() => _db.Dispose();
}

public sealed class PointeuseContextFactory : IPointeuseContextFactory
{
    public IDisposablePointeuseContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<PointeuseDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new DisposablePointeuseContext(new PointeuseDbContext(options));
    }
}
