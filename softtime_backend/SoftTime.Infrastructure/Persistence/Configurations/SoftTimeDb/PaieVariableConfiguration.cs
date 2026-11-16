using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

/// <summary>Schéma créé par migrations/versions/007_variables_paie.sql.</summary>
public sealed class PaieVariableConfiguration : IEntityTypeConfiguration<PaieVariable>
{
    public void Configure(EntityTypeBuilder<PaieVariable> builder)
    {
        builder.ToTable("T_PAIE_VARIABLE");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.BddSage).HasColumnName("BDD_SAGE").HasMaxLength(128).IsRequired(true);
        builder.Property(x => x.Variable).HasColumnName("VARIABLE").HasMaxLength(32).IsRequired(true);
        builder.Property(x => x.Actif).HasColumnName("ACTIF").IsRequired(true);
        builder.Property(x => x.CodePaie).HasColumnName("CODE_PAIE").HasMaxLength(32);
        builder.Property(x => x.Unite).HasColumnName("UNITE").HasMaxLength(1);
        builder.Property(x => x.ColCible).HasColumnName("COL_CIBLE").HasMaxLength(128);
        builder.Property(x => x.ModifieLe).HasColumnName("MODIFIE_LE").HasColumnType("datetime2");
        builder.Property(x => x.ModifiePar).HasColumnName("MODIFIE_PAR").HasMaxLength(256);
        builder.HasIndex(x => new { x.BddSage, x.Variable })
            .HasDatabaseName("UX_T_PAIE_VARIABLE_BDD_VARIABLE")
            .IsUnique();
    }
}
