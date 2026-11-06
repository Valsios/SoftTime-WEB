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

    public DbSet<T_ML_SALARIE_JOUR> SalarieJour => Set<T_ML_SALARIE_JOUR>();
    public DbSet<T_ML_SALARIE_SEMAINE> SalarieSemaine => Set<T_ML_SALARIE_SEMAINE>();
    public DbSet<T_ML_MODELE> Modele => Set<T_ML_MODELE>();
    public DbSet<T_ML_PREDICTION> Prediction => Set<T_ML_PREDICTION>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsOf<T_ML_MODELEConfiguration>();
    }
}
