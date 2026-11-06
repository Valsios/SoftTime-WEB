using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_PLANNING_SALConfiguration : IEntityTypeConfiguration<T_PLANNING_SAL>
{
    public void Configure(EntityTypeBuilder<T_PLANNING_SAL> builder)
    {
        builder.ToTable("T_PLANNING_SAL");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.BDD_POINTEUSE)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BDD_SAGE)
            .HasColumnName("BDD_SAGE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.DateP)
            .HasColumnName("DateP")
            .IsRequired(false);

        builder.Property(x => x.HA)
            .HasColumnName("HA")
            .IsRequired(false);

        builder.Property(x => x.HD)
            .HasColumnName("HD")
            .IsRequired(false);

        builder.Property(x => x.IDSAL)
            .HasColumnName("IDSAL");

        builder.Property(x => x.NoSHIFT)
            .HasColumnName("NoSHIFT")
            .IsRequired(false);

        builder.Property(x => x.OffP)
            .HasColumnName("OffP")
            .IsRequired(false);

        builder.Property(x => x.Pause)
            .HasColumnName("Pause")
            .IsRequired(false);
    }
}
