using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class HsValideeConfiguration : IEntityTypeConfiguration<HsValidee>
{
    public void Configure(EntityTypeBuilder<HsValidee> builder)
    {
        builder.ToTable("T_HS_VALIDER");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Absence)
            .HasColumnName("ABSENCE")
            .IsRequired(false);

        builder.Property(x => x.BddPointage)
            .HasColumnName("BDD_POINTAGE")
            .IsRequired(false);

        builder.Property(x => x.BddSage)
            .HasColumnName("BDD_SAGE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.DateDeb)
            .HasColumnName("DATE_DEB")
            .IsRequired(false);

        builder.Property(x => x.DateFin)
            .HasColumnName("DATE_FIN")
            .IsRequired(false);

        builder.Property(x => x.Dimanche)
            .HasColumnName("DIMANCHE")
            .IsRequired(false);

        builder.Property(x => x.Feries)
            .HasColumnName("FERIES")
            .IsRequired(false);

        builder.Property(x => x.Hs)
            .HasColumnName("HS")
            .IsRequired(false);

        builder.Property(x => x.Ht)
            .HasColumnName("HT")
            .IsRequired(false);

        builder.Property(x => x.Matricules)
            .HasColumnName("MATRICULES")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(x => x.Nuit)
            .HasColumnName("NUIT")
            .IsRequired(false);

        builder.Property(x => x.Retard)
            .HasColumnName("RETARD")
            .IsRequired(false);

        builder.Property(x => x.ValidationHs)
            .HasColumnName("VALIDATION_HS")
            .IsRequired(false);
    }
}
