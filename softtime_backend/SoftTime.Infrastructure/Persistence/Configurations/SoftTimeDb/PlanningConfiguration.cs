using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class PlanningConfiguration : IEntityTypeConfiguration<Planning>
{
    public void Configure(EntityTypeBuilder<Planning> builder)
    {
        builder.ToTable("T_PLANNING");
        builder.HasKey(x => x.IdPlanning);

        builder.Property(x => x.IdPlanning)
            .HasColumnName("IDPLANNING")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Ha)
            .HasColumnName("HA")
            .IsRequired(false);

        builder.Property(x => x.Hd)
            .HasColumnName("HD")
            .IsRequired(false);

        builder.Property(x => x.Intitule)
            .HasColumnName("Intitule")
            .IsRequired(false);

        builder.Property(x => x.NoShift)
            .HasColumnName("NoSHIFT")
            .IsRequired(false);

        builder.Property(x => x.Pause)
            .HasColumnName("PAUSE")
            .IsRequired(false);
    }
}
