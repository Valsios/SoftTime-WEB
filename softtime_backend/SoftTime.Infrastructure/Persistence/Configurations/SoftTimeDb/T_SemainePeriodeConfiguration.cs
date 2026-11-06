using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_SemainePeriodeConfiguration : IEntityTypeConfiguration<T_SemainePeriode>
{
    public void Configure(EntityTypeBuilder<T_SemainePeriode> builder)
    {
        builder.ToTable("T_SemainePeriode");
        builder.HasKey(x => x.OID);

        builder.Property(x => x.OID)
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
