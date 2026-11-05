using Microsoft.EntityFrameworkCore;
using SoftTime.Domain.Entities.Ml;

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

        modelBuilder.Entity<T_ML_SALARIE_JOUR>(e =>
        {
            e.ToTable("SalarieJour", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.BddSage).HasMaxLength(128).IsRequired();
            e.Property(x => x.Matricule).HasMaxLength(64).IsRequired();
            e.Property(x => x.Departement).HasMaxLength(128);
            e.Property(x => x.VersionSchema).HasMaxLength(32).IsRequired();
            e.Property(x => x.HeuresPlanifiees).HasPrecision(6, 2);
            e.Property(x => x.HeuresTravaillees).HasPrecision(6, 2);
            e.Property(x => x.AbsenceHeures).HasPrecision(6, 2);
            e.Property(x => x.HeuresSemaineContrat).HasPrecision(6, 2);
            e.Property(x => x.HeuresSupp4Semaines).HasPrecision(8, 2);
            e.HasIndex(x => new { x.BddSage, x.Matricule, x.Date }).IsUnique();
            e.HasIndex(x => new { x.BddSage, x.Date });
        });

        modelBuilder.Entity<T_ML_SALARIE_SEMAINE>(e =>
        {
            e.ToTable("SalarieSemaine", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.BddSage).HasMaxLength(128).IsRequired();
            e.Property(x => x.Matricule).HasMaxLength(64).IsRequired();
            e.Property(x => x.Departement).HasMaxLength(128);
            e.Property(x => x.VersionSchema).HasMaxLength(32).IsRequired();
            e.Property(x => x.HeuresPlanifiees).HasPrecision(8, 2);
            e.Property(x => x.HeuresTravaillees).HasPrecision(8, 2);
            e.Property(x => x.CongeJours).HasPrecision(4, 1);
            e.Property(x => x.HS).HasPrecision(8, 2);
            e.Property(x => x.Nuit).HasPrecision(8, 2);
            e.Property(x => x.Dimanche).HasPrecision(8, 2);
            e.Property(x => x.Ferie).HasPrecision(8, 2);
            e.Property(x => x.AbsenceHeures).HasPrecision(8, 2);
            e.Property(x => x.HsMoyenne4Semaines).HasPrecision(8, 2);
            e.Property(x => x.HsMoyenne8Semaines).HasPrecision(8, 2);
            e.Property(x => x.HeuresTravaillees4Semaines).HasPrecision(8, 2);
            e.Property(x => x.HeuresSemaineContrat).HasPrecision(6, 2);
            e.HasIndex(x => new { x.BddSage, x.Matricule, x.SemaineDebut }).IsUnique();
            e.HasIndex(x => new { x.BddSage, x.SemaineDebut });
        });

        modelBuilder.Entity<T_ML_MODELE>(e =>
        {
            e.ToTable("Modele", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.BddSage).HasMaxLength(128).IsRequired();
            e.Property(x => x.TypeModele).HasMaxLength(32).IsRequired();
            e.Property(x => x.Version).HasMaxLength(32).IsRequired();
            e.Property(x => x.Algorithme).HasMaxLength(64).IsRequired();
            e.Property(x => x.Statut).HasMaxLength(16).IsRequired();
            e.Property(x => x.SchemaHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.Commentaire).HasMaxLength(512);
            e.HasIndex(x => new { x.BddSage, x.TypeModele, x.EstActif })
                .HasFilter("[EstActif] = 1")
                .IsUnique();
        });

        modelBuilder.Entity<T_ML_PREDICTION>(e =>
        {
            e.ToTable("Prediction", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.BddSage).HasMaxLength(128).IsRequired();
            e.Property(x => x.Matricule).HasMaxLength(64).IsRequired();
            e.Property(x => x.TypeModele).HasMaxLength(32).IsRequired();
            e.Property(x => x.ValeurPrevue).HasPrecision(8, 2);
            e.Property(x => x.BorneBasse).HasPrecision(8, 2);
            e.Property(x => x.BorneHaute).HasPrecision(8, 2);
            e.HasOne<T_ML_MODELE>()
                .WithMany()
                .HasForeignKey(x => x.ModeleId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.BddSage, x.TypeModele, x.Matricule, x.Cible, x.ModeleId }).IsUnique();
            e.HasIndex(x => new { x.BddSage, x.TypeModele, x.Cible });
        });
    }
}
