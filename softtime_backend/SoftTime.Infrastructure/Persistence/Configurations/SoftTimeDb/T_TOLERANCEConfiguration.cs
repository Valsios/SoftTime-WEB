using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_TOLERANCEConfiguration : IEntityTypeConfiguration<T_TOLERANCE>
{
    public void Configure(EntityTypeBuilder<T_TOLERANCE> builder)
    {
        builder.ToTable("T_TOLERANCES");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.IDCATEGORIE)
            .HasColumnName("IDCATEGORIE");

        builder.Property(x => x.SORTIE)
            .HasColumnName("SORTIE")
            .IsRequired(false);

        builder.Property(x => x.TOLERANCE)
            .HasColumnName("TOLERANCE");

        builder.Property(x => x.TYPES_TOL)
            .HasColumnName("TYPES_TOL");
    }
}
