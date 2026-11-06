using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class ToleranceRetardConfiguration : IEntityTypeConfiguration<ToleranceRetard>
{
    public void Configure(EntityTypeBuilder<ToleranceRetard> builder)
    {
        builder.ToTable("T_TOLERANCES");
        builder.HasKey(x => x.Id);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<ToleranceRetard> builder)
    {
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.IdCategorie).HasColumnName("IDCATEGORIE");
        builder.Property(x => x.Sortie).HasColumnName("SORTIE").IsRequired(false);
        builder.Property(x => x.Tolerance).HasColumnName("TOLERANCE");
        builder.Property(x => x.TypesTol).HasColumnName("TYPES_TOL");
    }
}
