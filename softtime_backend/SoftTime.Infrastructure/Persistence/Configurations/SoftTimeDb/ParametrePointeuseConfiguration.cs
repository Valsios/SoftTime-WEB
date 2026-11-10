using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class ParametrePointeuseConfiguration : IEntityTypeConfiguration<ParametrePointeuse>
{
    public void Configure(EntityTypeBuilder<ParametrePointeuse> builder)
    {
        builder.ToTable("T_CLOCK");
        builder.HasKey(x => x.IdPoint);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<ParametrePointeuse> builder)
    {
        builder.Property(x => x.IdPoint).HasColumnName("IDPOINT").ValueGeneratedOnAdd();
        builder.Property(x => x.MultiPoint).HasColumnName("MULTIPOINT").IsRequired(false);
    }
}
