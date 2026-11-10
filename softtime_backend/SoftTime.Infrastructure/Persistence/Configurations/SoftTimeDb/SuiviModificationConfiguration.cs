using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class SuiviModificationConfiguration : IEntityTypeConfiguration<SuiviModification>
{
    public void Configure(EntityTypeBuilder<SuiviModification> builder)
    {
        builder.ToTable("suivi_SUIVI");
        builder.HasKey(x => x.Id);
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<SuiviModification> builder)
    {
        builder.Property(x => x.Actions).HasColumnName("_ACTIONS").HasMaxLength(128).IsRequired(false);
        builder.Property(x => x.Anciens).HasColumnName("_ANCIENS").IsRequired(false);
        builder.Property(x => x.Bases).HasColumnName("_BASES").IsRequired(false);
        builder.Property(x => x.Colonnes).HasColumnName("_COLONNES").IsRequired(false);
        builder.Property(x => x.Nouveaux).HasColumnName("_NOUVEAUX").IsRequired(false);
        builder.Property(x => x.Tables).HasColumnName("_TABLES").HasMaxLength(128).IsRequired(false);
        builder.Property(x => x.Users).HasColumnName("_USERS").IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<SuiviModification> builder)
    {
        builder.Property(x => x.Datetimes).HasColumnName("_DATETIMES").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<SuiviModification> builder)
    {
        builder.Property(x => x.Id).HasColumnName("Id").ValueGeneratedOnAdd();
    }
}
