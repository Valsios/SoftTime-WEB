using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class SemainePeriodeConfiguration : IEntityTypeConfiguration<SemainePeriode>
{
    public void Configure(EntityTypeBuilder<SemainePeriode> builder)
    {
        builder.ToTable("T_SemainePeriode");
        builder.HasKey(x => x.Oid);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<SemainePeriode> builder)
    {
        builder.Property(x => x.DebutSemaine).HasColumnName("DebutSemaine").IsRequired(false);
        builder.Property(x => x.FinSemaine).HasColumnName("FinSemaine").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<SemainePeriode> builder)
    {
        builder.Property(x => x.Oid).HasColumnName("OID").ValueGeneratedOnAdd();
        builder.Property(x => x.NoSemaine).HasColumnName("NoSemaine").IsRequired(false);
    }
}
