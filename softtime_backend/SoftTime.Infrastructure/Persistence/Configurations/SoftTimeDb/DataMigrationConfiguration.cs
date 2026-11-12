using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class DataMigrationConfiguration : IEntityTypeConfiguration<DataMigration>
{
    public void Configure(EntityTypeBuilder<DataMigration> builder)
    {
        builder.ToTable("T_MIGRATION_DONNEES");
        builder.HasKey(x => x.Code);
        builder.Property(x => x.Code).HasColumnName("CODE").HasMaxLength(100).IsRequired();
        builder.Property(x => x.DateApplication).HasColumnName("DATE_APPLICATION").IsRequired();
    }
}
