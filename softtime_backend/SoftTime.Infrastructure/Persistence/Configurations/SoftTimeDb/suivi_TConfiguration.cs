using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class suivi_TConfiguration : IEntityTypeConfiguration<suivi_T>
{
    public void Configure(EntityTypeBuilder<suivi_T> builder)
    {
        builder.ToTable("suivi_T");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.BASES)
            .HasColumnName("BASES")
            .IsRequired(false);

        builder.Property(x => x.S_COLONNES)
            .HasColumnName("S_COLONNES")
            .IsRequired(false);

        builder.Property(x => x.S_TABLES)
            .HasColumnName("S_TABLES")
            .IsRequired(false);

        builder.Property(x => x.S_TYPES)
            .HasColumnName("S_TYPES")
            .IsRequired(false);
    }
}
