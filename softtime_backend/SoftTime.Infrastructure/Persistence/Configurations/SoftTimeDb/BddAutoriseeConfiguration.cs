using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class BddAutoriseeConfiguration : IEntityTypeConfiguration<BddAutorisee>
{
    public void Configure(EntityTypeBuilder<BddAutorisee> builder)
    {
        builder.ToTable("T_BDD_AUTORISE");
        builder.HasKey(x => x.IdBddAutorise);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<BddAutorisee> builder)
    {
        builder.Property(x => x.IdBddAutorise).HasColumnName("IDBDDAUTORISE").ValueGeneratedOnAdd();
        builder.Property(x => x.IdBdd).HasColumnName("IDBDD");
        builder.Property(x => x.IdResponsable).HasColumnName("IDRESPONSABLE");
    }
}
