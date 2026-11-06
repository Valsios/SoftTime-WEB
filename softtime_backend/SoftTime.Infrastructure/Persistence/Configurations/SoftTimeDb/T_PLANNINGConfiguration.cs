using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_PLANNINGConfiguration : IEntityTypeConfiguration<T_PLANNING>
{
    public void Configure(EntityTypeBuilder<T_PLANNING> builder)
    {
        builder.ToTable("T_PLANNING");
        builder.HasKey(x => x.IDPLANNING);

        builder.Property(x => x.IDPLANNING)
            .HasColumnName("IDPLANNING")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.HA)
            .HasColumnName("HA")
            .IsRequired(false);

        builder.Property(x => x.HD)
            .HasColumnName("HD")
            .IsRequired(false);

        builder.Property(x => x.Intitule)
            .HasColumnName("Intitule")
            .IsRequired(false);

        builder.Property(x => x.NoSHIFT)
            .HasColumnName("NoSHIFT")
            .IsRequired(false);

        builder.Property(x => x.PAUSE)
            .HasColumnName("PAUSE")
            .IsRequired(false);
    }
}
