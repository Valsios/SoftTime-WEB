using Microsoft.EntityFrameworkCore;

using SoftTime.Domain.Entities.Pointeuse;

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
