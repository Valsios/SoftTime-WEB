using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class SemaineConfiguration : IEntityTypeConfiguration<Semaine>
{
    public void Configure(EntityTypeBuilder<Semaine> builder)
    {
        builder.ToTable("T_Semaine");
        builder.HasNoKey();
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<Semaine> builder)
    {
        builder.Property(x => x.CodeSemain).HasColumnName("CodeSemain").IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<Semaine> builder)
    {
        builder.Property(x => x.DateDeb).HasColumnName("DateDeb").IsRequired(false);
        builder.Property(x => x.DateFin).HasColumnName("DateFin").IsRequired(false);
    }
}
