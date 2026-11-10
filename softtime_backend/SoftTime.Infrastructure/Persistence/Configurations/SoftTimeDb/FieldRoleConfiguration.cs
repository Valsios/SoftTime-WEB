using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class FieldRoleConfiguration : IEntityTypeConfiguration<FieldRole>
{
    public void Configure(EntityTypeBuilder<FieldRole> builder)
    {
        builder.ToTable("T_FIELD_ROLE");
        builder.HasKey(x => x.Code);
        ConfigureTextColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<FieldRole> builder)
    {
        builder.Property(x => x.Code).HasColumnName("Code").HasMaxLength(64).IsRequired(true).ValueGeneratedNever();
        builder.Property(x => x.AutoMappingPatterns)
            .HasColumnName("AutoMappingPatterns")
            .HasMaxLength(256)
            .IsRequired(false);
        builder.Property(x => x.EntityKind).HasColumnName("EntityKind").HasMaxLength(32).IsRequired(false);
        builder.Property(x => x.Label).HasColumnName("Label").HasMaxLength(128).IsRequired(false);
        builder.Property(x => x.SystemType).HasColumnName("SystemType").HasMaxLength(16).IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<FieldRole> builder)
    {
        builder.Property(x => x.IsRequired).HasColumnName("IsRequired");
        builder.Property(x => x.SortOrder).HasColumnName("SortOrder");
    }
}
