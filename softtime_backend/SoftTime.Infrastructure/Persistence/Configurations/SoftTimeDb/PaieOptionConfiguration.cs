using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

/// <summary>Schéma créé par migrations/versions/008_options_paie.sql.</summary>
public sealed class PaieOptionConfiguration : IEntityTypeConfiguration<PaieOption>
{
    public void Configure(EntityTypeBuilder<PaieOption> builder)
    {
        builder.ToTable("T_PAIE_OPTION");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.BddSage).HasColumnName("BDD_SAGE").HasMaxLength(128).IsRequired(true);
        builder.Property(x => x.Cle).HasColumnName("CLE").HasMaxLength(64).IsRequired(true);
        builder.Property(x => x.Valeur).HasColumnName("VALEUR").HasMaxLength(256).IsRequired(true);
        builder.Property(x => x.ModifieLe).HasColumnName("MODIFIE_LE").HasColumnType("datetime2");
        builder.Property(x => x.ModifiePar).HasColumnName("MODIFIE_PAR").HasMaxLength(256);
        builder.HasIndex(x => new { x.BddSage, x.Cle })
            .HasDatabaseName("UX_T_PAIE_OPTION_BDD_CLE")
            .IsUnique();
    }
}
