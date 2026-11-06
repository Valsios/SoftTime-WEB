using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class SourceFieldMappingConfiguration : IEntityTypeConfiguration<SourceFieldMapping>
{
    public void Configure(EntityTypeBuilder<SourceFieldMapping> builder)
    {
        builder.ToTable("T_SOURCE_FIELD_MAPPING");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.EntityMappingId)
            .HasColumnName("EntityMappingId");

        builder.Property(x => x.FieldRoleCode)
            .HasColumnName("FieldRoleCode")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(x => x.SourceColumn)
            .HasColumnName("SourceColumn")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.HasIndex(x => new { x.EntityMappingId, x.FieldRoleCode })
            .HasDatabaseName("IX_T_SOURCE_FIELD_MAPPING_EntityMappingId_FieldRoleCode")
            .IsUnique();

        builder.HasOne<SourceEntityMapping>()
            .WithMany(x => x.Fields)
            .HasForeignKey(x => x.EntityMappingId)
            .IsRequired(true)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
