using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class DroitAccesConfiguration : IEntityTypeConfiguration<DroitAcces>
{
    public void Configure(EntityTypeBuilder<DroitAcces> builder)
    {
        builder.ToTable("T_DROIT");
        builder.HasKey(x => x.IdDroit);
        ConfigureTextColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<DroitAcces> builder)
    {
        builder.Property(x => x.Droit).HasColumnName("DROIT").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<DroitAcces> builder)
    {
        builder.Property(x => x.IdDroit).HasColumnName("IDDROIT").ValueGeneratedOnAdd();
    }
}
