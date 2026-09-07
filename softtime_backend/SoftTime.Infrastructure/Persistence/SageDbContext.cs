using Microsoft.EntityFrameworkCore;
using SoftTime.Domain.Entities.Sage;
using SoftTime.Domain.Repositories;

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

public sealed class DisposableSageContext : IDisposableSageContext
{
    private readonly SageDbContext _db;
    public DisposableSageContext(SageDbContext db) => _db = db;
    public IQueryable<T_SAL> Employees => _db.T_SAL;
    public IQueryable<T_GHR> Events => _db.T_GHR;
    public IQueryable<T_GHRSAL> EmployeeEvents => _db.T_GHRSAL;
    public IQueryable<T_CST> Constants => _db.T_CST;
    public IQueryable<T_DEPARTEMENT> Departments => _db.T_DEPARTEMENT;
    public IQueryable<T_HST_AFFECTATION> Affectations => _db.T_HST_AFFECTATION;
    public IQueryable<T_GHRCAL_SOCIETE> CompanyCalendar => _db.T_GHRCAL_SOCIETE;
    public string ConnectionString => _db.Database.GetConnectionString() ?? string.Empty;
    public SageDbContext Db => _db;
    public void Dispose() => _db.Dispose();
}

public sealed class SageContextFactory : ISageContextFactory
{
    public IDisposableSageContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SageDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new DisposableSageContext(new SageDbContext(options));
    }
}
