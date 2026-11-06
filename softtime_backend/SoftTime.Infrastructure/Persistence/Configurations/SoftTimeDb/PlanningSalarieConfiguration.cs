using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class PlanningSalarieConfiguration : IEntityTypeConfiguration<PlanningSalarie>
{
    public void Configure(EntityTypeBuilder<PlanningSalarie> builder)
    {
        builder.ToTable("T_PLANNING_SAL");
        builder.HasKey(x => x.Id);
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<PlanningSalarie> builder)
    {
        builder.Property(x => x.BddPointeuse).HasColumnName("BDD_POINTEUSE").IsRequired(false);
        builder.Property(x => x.BddSage).HasColumnName("BDD_SAGE").HasMaxLength(128).IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<PlanningSalarie> builder)
    {
        builder.Property(x => x.DateP).HasColumnName("DateP").IsRequired(false);
        builder.Property(x => x.Ha).HasColumnName("HA").IsRequired(false);
        builder.Property(x => x.Hd).HasColumnName("HD").IsRequired(false);
        builder.Property(x => x.Pause).HasColumnName("Pause").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<PlanningSalarie> builder)
    {
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.IdSal).HasColumnName("IDSAL");
        builder.Property(x => x.NoShift).HasColumnName("NoSHIFT").IsRequired(false);
        builder.Property(x => x.OffP).HasColumnName("OffP").IsRequired(false);
    }
}
