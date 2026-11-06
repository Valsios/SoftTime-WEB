using Microsoft.EntityFrameworkCore;

using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Persistence;

public class SageDbContext : DbContext
{
    public SageDbContext(DbContextOptions<SageDbContext> options) : base(options) { }

    public DbSet<T_SAL> T_SAL => Set<T_SAL>();
    public DbSet<T_GHR> T_GHR => Set<T_GHR>();
    public DbSet<T_GHRSAL> T_GHRSAL => Set<T_GHRSAL>();
    public DbSet<T_CST> T_CST => Set<T_CST>();
    public DbSet<T_DEPARTEMENT> T_DEPARTEMENT => Set<T_DEPARTEMENT>();
    public DbSet<T_HST_AFFECTATION> T_HST_AFFECTATION => Set<T_HST_AFFECTATION>();
    public DbSet<T_GHRCAL_SOCIETE> T_GHRCAL_SOCIETE => Set<T_GHRCAL_SOCIETE>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<T_CST>().HasKey(e => new { e.CodeConstante, e.NoOrdre });
    }
}
