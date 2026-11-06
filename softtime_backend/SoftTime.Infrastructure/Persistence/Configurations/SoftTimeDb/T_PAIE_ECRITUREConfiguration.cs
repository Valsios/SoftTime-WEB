using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_PAIE_ECRITUREConfiguration : IEntityTypeConfiguration<T_PAIE_ECRITURE>
{
    public void Configure(EntityTypeBuilder<T_PAIE_ECRITURE> builder)
    {
        builder.ToTable("T_PAIE_ECRITURE");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.BDD_SAGE)
            .HasColumnName("BDD_SAGE")
            .HasMaxLength(128)
            .IsRequired(true);

        builder.Property(x => x.CATEGORIE)
            .HasColumnName("CATEGORIE")
            .HasMaxLength(32)
            .IsRequired(true);

        builder.Property(x => x.COL_MATRICULE)
            .HasColumnName("COL_MATRICULE")
            .HasMaxLength(128)
            .IsRequired(true);

        builder.Property(x => x.COL_VALEUR)
            .HasColumnName("COL_VALEUR")
            .HasMaxLength(128)
            .IsRequired(true);

        builder.Property(x => x.TABLE_CIBLE)
            .HasColumnName("TABLE_CIBLE")
            .HasMaxLength(256)
            .IsRequired(true);

        builder.HasIndex(x => new { x.BDD_SAGE, x.CATEGORIE })
            .HasDatabaseName("IX_T_PAIE_ECRITURE_BDD_SAGE_CATEGORIE")
            .IsUnique();
    }
}
