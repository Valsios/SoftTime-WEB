using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class PaieEcritureConfiguration : IEntityTypeConfiguration<PaieEcriture>
{
    public void Configure(EntityTypeBuilder<PaieEcriture> builder)
    {
        builder.ToTable("T_PAIE_ECRITURE");
        builder.HasKey(x => x.Id);
        ConfigureTextColumns(builder);
        ConfigureValueColumns(builder);
        ConfigureIndexes(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<PaieEcriture> builder)
    {
        builder.Property(x => x.BddSage).HasColumnName("BDD_SAGE").HasMaxLength(128).IsRequired(true);
        builder.Property(x => x.Categorie).HasColumnName("CATEGORIE").HasMaxLength(32).IsRequired(true);
        builder.Property(x => x.ColMatricule).HasColumnName("COL_MATRICULE").HasMaxLength(128).IsRequired(true);
        builder.Property(x => x.ColValeur).HasColumnName("COL_VALEUR").HasMaxLength(128).IsRequired(true);
        builder.Property(x => x.TableCible).HasColumnName("TABLE_CIBLE").HasMaxLength(256).IsRequired(true);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<PaieEcriture> builder)
    {
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
    }

    private static void ConfigureIndexes(EntityTypeBuilder<PaieEcriture> builder)
    {
        builder.HasIndex(x => new { x.BddSage, x.Categorie })
            .HasDatabaseName("IX_T_PAIE_ECRITURE_BDD_SAGE_CATEGORIE")
            .IsUnique();
    }
}
