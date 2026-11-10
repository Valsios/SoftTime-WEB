using Microsoft.EntityFrameworkCore;

using SoftTime.Domain.Entities.Pointeuse;
using SoftTime.Infrastructure.Persistence.Configurations;
using SoftTime.Infrastructure.Persistence.Configurations.PointeuseDb;

namespace SoftTime.Infrastructure.Persistence;

public class PointeuseDbContext : DbContext
{
    public PointeuseDbContext(DbContextOptions<PointeuseDbContext> options) : base(options) { }

    public DbSet<CheckInOut> CheckInOuts => Set<CheckInOut>();
    public DbSet<UserInfo> UserInfos => Set<UserInfo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsOf<UserInfoConfiguration>();
}
