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

        builder.Property(x => x.Oid)
            .HasColumnName("OID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.DebutSemaine)
            .HasColumnName("DebutSemaine")
            .IsRequired(false);

        builder.Property(x => x.FinSemaine)
            .HasColumnName("FinSemaine")
            .IsRequired(false);

        builder.Property(x => x.NoSemaine)
            .HasColumnName("NoSemaine")
            .IsRequired(false);
    }
}
