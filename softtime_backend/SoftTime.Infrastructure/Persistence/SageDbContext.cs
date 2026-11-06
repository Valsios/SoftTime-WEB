using Microsoft.EntityFrameworkCore;

using SoftTime.Domain.Entities.Sage;
using SoftTime.Infrastructure.Persistence.Configurations;
using SoftTime.Infrastructure.Persistence.Configurations.SageDb;

namespace SoftTime.Infrastructure.Persistence;

public class SageDbContext : DbContext
{
    public SageDbContext(DbContextOptions<SageDbContext> options) : base(options) { }

    public DbSet<SalarieSage> SalarieSages => Set<SalarieSage>();
    public DbSet<EvenementSage> EvenementSages => Set<EvenementSage>();
    public DbSet<EvenementSalarieSage> EvenementSalarieSages => Set<EvenementSalarieSage>();
    public DbSet<ConstanteSage> ConstanteSages => Set<ConstanteSage>();
    public DbSet<DepartementSage> DepartementSages => Set<DepartementSage>();
    public DbSet<HistoriqueAffectationSage> HistoriqueAffectationSages => Set<HistoriqueAffectationSage>();
    public DbSet<CalendrierSocieteSage> CalendrierSocieteSages => Set<CalendrierSocieteSage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsOf<SalarieSageConfiguration>();
}
