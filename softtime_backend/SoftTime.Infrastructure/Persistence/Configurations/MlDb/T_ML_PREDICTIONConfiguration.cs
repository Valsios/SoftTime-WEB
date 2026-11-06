using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Ml;

namespace SoftTime.Infrastructure.Persistence.Configurations.MlDb;

public sealed class T_ML_PREDICTIONConfiguration : IEntityTypeConfiguration<T_ML_PREDICTION>
{
    public void Configure(EntityTypeBuilder<T_ML_PREDICTION> builder)
    {
        builder.ToTable("Prediction");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.BddSage)
            .HasColumnName("BddSage")
            .HasMaxLength(128)
            .IsRequired(true);

        builder.Property(x => x.BorneBasse)
            .HasColumnName("BorneBasse")
            .HasPrecision(8, 2)
            .IsRequired(false);

        builder.Property(x => x.BorneHaute)
            .HasColumnName("BorneHaute")
            .HasPrecision(8, 2)
            .IsRequired(false);

        builder.Property(x => x.Cible)
            .HasColumnName("Cible");

        builder.Property(x => x.GenereLe)
            .HasColumnName("GenereLe");

        builder.Property(x => x.Matricule)
            .HasColumnName("Matricule")
            .HasMaxLength(64)
            .IsRequired(true);

        builder.Property(x => x.ModeleId)
            .HasColumnName("ModeleId");

        builder.Property(x => x.NiveauRisque)
            .HasColumnName("NiveauRisque");

        builder.Property(x => x.Probabilite)
            .HasColumnName("Probabilite")
            .IsRequired(false);

        builder.Property(x => x.TypeModele)
            .HasColumnName("TypeModele")
            .HasMaxLength(32)
            .IsRequired(true);

        builder.Property(x => x.ValeurPrevue)
            .HasColumnName("ValeurPrevue")
            .HasPrecision(8, 2)
            .IsRequired(false);

        builder.HasIndex(x => x.ModeleId)
            .HasDatabaseName("IX_Prediction_ModeleId");

        builder.HasIndex(x => new { x.BddSage, x.TypeModele, x.Cible })
            .HasDatabaseName("IX_Prediction_BddSage_TypeModele_Cible");

        builder.HasIndex(x => new { x.BddSage, x.TypeModele, x.Matricule, x.Cible, x.ModeleId })
            .HasDatabaseName("IX_Prediction_BddSage_TypeModele_Matricule_Cible_ModeleId")
            .IsUnique();

        builder.HasOne<T_ML_MODELE>()
            .WithMany()
            .HasForeignKey(x => x.ModeleId)
            .IsRequired(true)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
