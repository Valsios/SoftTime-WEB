using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_HS_VALIDERConfiguration : IEntityTypeConfiguration<T_HS_VALIDER>
{
    public void Configure(EntityTypeBuilder<T_HS_VALIDER> builder)
    {
        builder.ToTable("T_HS_VALIDER");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ABSENCE)
            .HasColumnName("ABSENCE")
            .IsRequired(false);

        builder.Property(x => x.BDD_POINTAGE)
            .HasColumnName("BDD_POINTAGE")
            .IsRequired(false);

        builder.Property(x => x.BDD_SAGE)
            .HasColumnName("BDD_SAGE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.DATE_DEB)
            .HasColumnName("DATE_DEB")
            .IsRequired(false);

        builder.Property(x => x.DATE_FIN)
            .HasColumnName("DATE_FIN")
            .IsRequired(false);

        builder.Property(x => x.DIMANCHE)
            .HasColumnName("DIMANCHE")
            .IsRequired(false);

        builder.Property(x => x.FERIES)
            .HasColumnName("FERIES")
            .IsRequired(false);

        builder.Property(x => x.HS)
            .HasColumnName("HS")
            .IsRequired(false);

        builder.Property(x => x.HT)
            .HasColumnName("HT")
            .IsRequired(false);

        builder.Property(x => x.MATRICULES)
            .HasColumnName("MATRICULES")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(x => x.NUIT)
            .HasColumnName("NUIT")
            .IsRequired(false);

        builder.Property(x => x.RETARD)
            .HasColumnName("RETARD")
            .IsRequired(false);

        builder.Property(x => x.VALIDATION_HS)
            .HasColumnName("VALIDATION_HS")
            .IsRequired(false);
    }
}
