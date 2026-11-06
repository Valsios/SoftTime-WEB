using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class HsSemaineConfiguration : IEntityTypeConfiguration<HsSemaine>
{
    public void Configure(EntityTypeBuilder<HsSemaine> builder)
    {
        builder.ToTable("T_HSSemaine");
        builder.HasKey(x => x.Oid);
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<HsSemaine> builder)
    {
        builder.Property(x => x.BddPointeuse).HasColumnName("BDD_POINTEUSE").IsRequired(false);
        builder.Property(x => x.BddSage).HasColumnName("BDD_SAGE").HasMaxLength(128).IsRequired(false);
        builder.Property(x => x.Matricule).HasColumnName("Matricule").HasMaxLength(64).IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<HsSemaine> builder)
    {
        builder.Property(x => x.PeriodeDebut).HasColumnName("PeriodeDebut").IsRequired(false);
        builder.Property(x => x.PeriodeFin).HasColumnName("PeriodeFin").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<HsSemaine> builder)
    {
        builder.Property(x => x.Oid).HasColumnName("OID").ValueGeneratedOnAdd();
        builder.Property(x => x.Hs).HasColumnName("HS").IsRequired(false);
        builder.Property(x => x.Hs1).HasColumnName("HS1").IsRequired(false);
        builder.Property(x => x.Hs2).HasColumnName("HS2").IsRequired(false);
        builder.Property(x => x.SalNum).HasColumnName("SalNum").IsRequired(false);
        builder.Property(x => x.Semaine).HasColumnName("Semaine").IsRequired(false);
        builder.Property(x => x.TotalHeure).HasColumnName("TotalHeure").IsRequired(false);
    }
}
