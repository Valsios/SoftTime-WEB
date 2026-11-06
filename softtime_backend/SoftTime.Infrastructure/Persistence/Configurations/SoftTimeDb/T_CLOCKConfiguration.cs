using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_CLOCKConfiguration : IEntityTypeConfiguration<T_CLOCK>
{
    public void Configure(EntityTypeBuilder<T_CLOCK> builder)
    {
        builder.ToTable("T_CLOCK");
        builder.HasKey(x => x.IDPOINT);

        builder.Property(x => x.IDPOINT)
            .HasColumnName("IDPOINT")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.MULTIPOINT)
            .HasColumnName("MULTIPOINT")
            .IsRequired(false);
    }
}
