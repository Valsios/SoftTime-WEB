using Microsoft.EntityFrameworkCore;

using SoftTime.Domain.Entities.Pointeuse;
using SoftTime.Infrastructure.Persistence.Configurations;
using SoftTime.Infrastructure.Persistence.Configurations.PointeuseDb;

namespace SoftTime.Infrastructure.Persistence;

public class PointeuseDbContext : DbContext
{
    public PointeuseDbContext(DbContextOptions<PointeuseDbContext> options) : base(options) { }

    public DbSet<CHECKINOUT> CHECKINOUT => Set<CHECKINOUT>();
    public DbSet<USERINFO> USERINFO => Set<USERINFO>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsOf<USERINFOConfiguration>();
}
