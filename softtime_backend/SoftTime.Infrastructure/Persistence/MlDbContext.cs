using Microsoft.EntityFrameworkCore;

using SoftTime.Domain.Entities.Ml;
using SoftTime.Infrastructure.Persistence.Configurations;
using SoftTime.Infrastructure.Persistence.Configurations.MlDb;

namespace SoftTime.Infrastructure.Persistence;

/// <summary>
/// Contexte de la base analytique « model-ready ». Il vit dans le schéma <c>ml</c> de la base
/// SoftTime par défaut ; une chaîne <c>SoftTimeMl</c> permet de le déplacer vers une base dédiée
/// sans changer le code. Les tables métier ne sont jamais exposées ici.
/// </summary>
public class MlDbContext : DbContext
{
    public const string Schema = "ml";

    public MlDbContext(DbContextOptions<MlDbContext> options) : base(options) { }

    public DbSet<MlSalarieJour> SalarieJour => Set<MlSalarieJour>();
    public DbSet<MlSalarieSemaine> SalarieSemaine => Set<MlSalarieSemaine>();
    public DbSet<MlModele> Modele => Set<MlModele>();
    public DbSet<MlPrediction> Prediction => Set<MlPrediction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsOf<MlModeleConfiguration>();
    }
}
