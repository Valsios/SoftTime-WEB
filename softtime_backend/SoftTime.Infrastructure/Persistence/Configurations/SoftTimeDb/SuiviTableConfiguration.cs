using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class SuiviTableConfiguration : IEntityTypeConfiguration<SuiviTable>
{
    public void Configure(EntityTypeBuilder<SuiviTable> builder)
    {
        builder.ToTable("suivi_T");
        builder.HasKey(x => x.Id);
        ConfigureTextColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<SuiviTable> builder)
    {
        builder.Property(x => x.Bases).HasColumnName("BASES").IsRequired(false);
        builder.Property(x => x.SColonnes).HasColumnName("S_COLONNES").IsRequired(false);
        builder.Property(x => x.STables).HasColumnName("S_TABLES").IsRequired(false);
        builder.Property(x => x.STypes).HasColumnName("S_TYPES").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<SuiviTable> builder)
    {
        builder.Property(x => x.Id).HasColumnName("Id").ValueGeneratedOnAdd();
    }
}
