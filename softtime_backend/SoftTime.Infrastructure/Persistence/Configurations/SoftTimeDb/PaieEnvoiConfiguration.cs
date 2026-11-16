using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

/// <summary>Schéma créé par migrations/versions/007_variables_paie.sql.</summary>
public sealed class PaieEnvoiConfiguration : IEntityTypeConfiguration<PaieEnvoi>
{
    public void Configure(EntityTypeBuilder<PaieEnvoi> builder)
    {
        builder.ToTable("T_PAIE_ENVOI");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.BddSage).HasColumnName("BDD_SAGE").HasMaxLength(128).IsRequired(true);
        builder.Property(x => x.PeriodeDebut).HasColumnName("PERIODE_DEBUT").HasColumnType("date");
        builder.Property(x => x.PeriodeFin).HasColumnName("PERIODE_FIN").HasColumnType("date");
        builder.Property(x => x.EnvoyeLe).HasColumnName("ENVOYE_LE").HasColumnType("datetime2");
        builder.Property(x => x.EnvoyePar).HasColumnName("ENVOYE_PAR").HasMaxLength(256);
        builder.Property(x => x.Statut).HasColumnName("STATUT").HasMaxLength(16).IsRequired(true);
        builder.Property(x => x.Lignes).HasColumnName("LIGNES");
        builder.Property(x => x.TypeBase).HasColumnName("TYPE_BASE").HasMaxLength(16).IsRequired(true);
        builder.HasIndex(x => new { x.BddSage, x.PeriodeDebut, x.PeriodeFin })
            .HasDatabaseName("IX_T_PAIE_ENVOI_BDD_PERIODE");
    }
}
