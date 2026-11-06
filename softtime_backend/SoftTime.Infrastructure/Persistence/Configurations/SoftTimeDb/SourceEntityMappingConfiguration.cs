using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class SourceEntityMappingConfiguration : IEntityTypeConfiguration<SourceEntityMapping>
{
    public void Configure(EntityTypeBuilder<SourceEntityMapping> builder)
    {
        builder.ToTable("T_SOURCE_ENTITY_MAPPING");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.EntityKind)
            .HasColumnName("EntityKind")
            .HasMaxLength(32)
            .IsRequired(false);

        builder.Property(x => x.PointeuseDbId)
            .HasColumnName("PointeuseDbId")
            .IsRequired(false);

        builder.Property(x => x.SageDbId)
            .HasColumnName("SageDbId")
            .IsRequired(false);

        builder.Property(x => x.SourceTable)
            .HasColumnName("SourceTable")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.SystemType)
            .HasColumnName("SystemType")
            .HasMaxLength(16)
            .IsRequired(false);

        builder.HasIndex(x => new { x.PointeuseDbId, x.EntityKind })
            .HasDatabaseName("IX_T_SOURCE_ENTITY_MAPPING_PointeuseDbId_EntityKind")
            .IsUnique()
            .HasFilter("[PointeuseDbId] IS NOT NULL");

        builder.HasIndex(x => new { x.SageDbId, x.EntityKind })
            .HasDatabaseName("IX_T_SOURCE_ENTITY_MAPPING_SageDbId_EntityKind")
            .IsUnique()
            .HasFilter("[SageDbId] IS NOT NULL");
    }
}
