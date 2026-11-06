using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class HeureAnomalieConfiguration : IEntityTypeConfiguration<HeureAnomalie>
{
    public void Configure(EntityTypeBuilder<HeureAnomalie> builder)
    {
        builder.ToTable("T_HEUREANOMALIE");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.AbsAm)
            .HasColumnName("ABS_AM")
            .IsRequired(false);

        builder.Property(x => x.AbsPm)
            .HasColumnName("ABS_PM")
            .IsRequired(false);

        builder.Property(x => x.BddPointeuse)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BddSage)
            .HasColumnName("BDD_SAGE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.DatePointageIn)
            .HasColumnName("DATE_POINTAGE_IN")
            .IsRequired(false);

        builder.Property(x => x.DatePointageOut)
            .HasColumnName("DATE_POINTAGE_OUT")
            .IsRequired(false);

        builder.Property(x => x.Feries)
            .HasColumnName("FERIES")
            .IsRequired(false);

        builder.Property(x => x.FeriesAm)
            .HasColumnName("FERIES_AM")
            .IsRequired(false);

        builder.Property(x => x.FeriesPm)
            .HasColumnName("FERIES_PM")
            .IsRequired(false);

        builder.Property(x => x.H1)
            .HasColumnName("H1")
            .IsRequired(false);

        builder.Property(x => x.H2)
            .HasColumnName("H2")
            .IsRequired(false);

        builder.Property(x => x.HEntree)
            .HasColumnName("HENTREE")
            .IsRequired(false);

        builder.Property(x => x.Hpe)
            .HasColumnName("HPE")
            .IsRequired(false);

        builder.Property(x => x.Hps)
            .HasColumnName("HPS")
            .IsRequired(false);

        builder.Property(x => x.HSortie)
            .HasColumnName("HSORTIE")
            .IsRequired(false);

        builder.Property(x => x.IntituleAbsence)
            .HasColumnName("INTITULE_ABSENCE")
            .IsRequired(false);

        builder.Property(x => x.MatriculeSage)
            .HasColumnName("MATRICULE_SAGE")
            .HasMaxLength(64)
            .IsRequired(false);
    }
}
