using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class MajorationConfiguration : IEntityTypeConfiguration<Majoration>
{
    public void Configure(EntityTypeBuilder<Majoration> builder)
    {
        builder.ToTable("T_MAJORATION");
        builder.HasKey(x => x.Id);
        ConfigureTextColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<Majoration> builder)
    {
        builder.Property(x => x.BddPointeuse).HasColumnName("BDD_POINTEUSE").IsRequired(false);
        builder.Property(x => x.BddSage).HasColumnName("BDD_SAGE").IsRequired(false);
        builder.Property(x => x.Mojoration).HasColumnName("Mojoration").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<Majoration> builder)
    {
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.Cotation).HasColumnName("Cotation").HasPrecision(18, 2).IsRequired(false);
    }
}
