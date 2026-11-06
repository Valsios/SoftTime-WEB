using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_HEUREANOMALIEConfiguration : IEntityTypeConfiguration<T_HEUREANOMALIE>
{
    public void Configure(EntityTypeBuilder<T_HEUREANOMALIE> builder)
    {
        builder.ToTable("T_HEUREANOMALIE");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ABS_AM)
            .HasColumnName("ABS_AM")
            .IsRequired(false);

        builder.Property(x => x.ABS_PM)
            .HasColumnName("ABS_PM")
            .IsRequired(false);

        builder.Property(x => x.BDD_POINTEUSE)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BDD_SAGE)
            .HasColumnName("BDD_SAGE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.DATE_POINTAGE_IN)
            .HasColumnName("DATE_POINTAGE_IN")
            .IsRequired(false);

        builder.Property(x => x.DATE_POINTAGE_OUT)
            .HasColumnName("DATE_POINTAGE_OUT")
            .IsRequired(false);

        builder.Property(x => x.FERIES)
            .HasColumnName("FERIES")
            .IsRequired(false);

        builder.Property(x => x.FERIES_AM)
            .HasColumnName("FERIES_AM")
            .IsRequired(false);

        builder.Property(x => x.FERIES_PM)
            .HasColumnName("FERIES_PM")
            .IsRequired(false);

        builder.Property(x => x.H1)
            .HasColumnName("H1")
            .IsRequired(false);

        builder.Property(x => x.H2)
            .HasColumnName("H2")
            .IsRequired(false);

        builder.Property(x => x.HENTREE)
            .HasColumnName("HENTREE")
            .IsRequired(false);

        builder.Property(x => x.HPE)
            .HasColumnName("HPE")
            .IsRequired(false);

        builder.Property(x => x.HPS)
            .HasColumnName("HPS")
            .IsRequired(false);

        builder.Property(x => x.HSORTIE)
            .HasColumnName("HSORTIE")
            .IsRequired(false);

        builder.Property(x => x.INTITULE_ABSENCE)
            .HasColumnName("INTITULE_ABSENCE")
            .IsRequired(false);

        builder.Property(x => x.MATRICULE_SAGE)
            .HasColumnName("MATRICULE_SAGE")
            .HasMaxLength(64)
            .IsRequired(false);
    }
}
