using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Ml;

namespace SoftTime.Infrastructure.Persistence.Configurations.MlDb;

public sealed class MlModeleConfiguration : IEntityTypeConfiguration<MlModele>
{
    public void Configure(EntityTypeBuilder<MlModele> builder)
    {
        builder.ToTable("Modele");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Algorithme)
            .HasColumnName("Algorithme")
            .HasMaxLength(64)
            .IsRequired(true);

        builder.Property(x => x.BddSage)
            .HasColumnName("BddSage")
            .HasMaxLength(128)
            .IsRequired(true);

        builder.Property(x => x.Commentaire)
            .HasColumnName("Commentaire")
            .HasMaxLength(512)
            .IsRequired(false);

        builder.Property(x => x.EntraineLe)
            .HasColumnName("EntraineLe");

        builder.Property(x => x.EstActif)
            .HasColumnName("EstActif");

        builder.Property(x => x.MetriquesJson)
            .HasColumnName("MetriquesJson")
            .IsRequired(false);

        builder.Property(x => x.ModeleBinaire)
            .HasColumnName("ModeleBinaire")
            .IsRequired(false);

        builder.Property(x => x.NbLignes)
            .HasColumnName("NbLignes");

        builder.Property(x => x.NbPositifs)
            .HasColumnName("NbPositifs")
            .IsRequired(false);

        builder.Property(x => x.PeriodeDebut)
            .HasColumnName("PeriodeDebut");

        builder.Property(x => x.PeriodeFin)
            .HasColumnName("PeriodeFin");

        builder.Property(x => x.SchemaHash)
            .HasColumnName("SchemaHash")
            .HasMaxLength(64)
            .IsRequired(true);

        builder.Property(x => x.Statut)
            .HasColumnName("Statut")
            .HasMaxLength(16)
            .IsRequired(true);

        builder.Property(x => x.TypeModele)
            .HasColumnName("TypeModele")
            .HasMaxLength(32)
            .IsRequired(true);

        builder.Property(x => x.Version)
            .HasColumnName("Version")
            .HasMaxLength(32)
            .IsRequired(true);

        builder.HasIndex(x => new { x.BddSage, x.TypeModele, x.EstActif })
            .HasDatabaseName("IX_Modele_BddSage_TypeModele_EstActif")
            .IsUnique()
            .HasFilter("[EstActif] = 1");
    }
}
