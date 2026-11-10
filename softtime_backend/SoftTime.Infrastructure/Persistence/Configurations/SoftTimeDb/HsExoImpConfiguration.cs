using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class HsExoImpConfiguration : IEntityTypeConfiguration<HsExoImp>
{
    public void Configure(EntityTypeBuilder<HsExoImp> builder)
    {
        builder.ToTable("T_HSExoImp");
        builder.HasKey(x => x.Oid);
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<HsExoImp> builder)
    {
        builder.Property(x => x.BddPointeuse).HasColumnName("BDD_POINTEUSE").IsRequired(false);
        builder.Property(x => x.BddSage).HasColumnName("BDD_SAGE").HasMaxLength(128).IsRequired(false);
        builder.Property(x => x.Matricule).HasColumnName("Matricule").HasMaxLength(64).IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<HsExoImp> builder)
    {
        builder.Property(x => x.PeriodeDebut).HasColumnName("PeriodeDebut").IsRequired(false);
        builder.Property(x => x.PeriodeFin).HasColumnName("PeriodeFin").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<HsExoImp> builder)
    {
        builder.Property(x => x.Oid).HasColumnName("OID").ValueGeneratedOnAdd();
        builder.Property(x => x.Absence).HasColumnName("ABSENCE").IsRequired(false);
        builder.Property(x => x.Dim).HasColumnName("DIM").IsRequired(false);
        builder.Property(x => x.Exo130).HasColumnName("EXO130").IsRequired(false);
        builder.Property(x => x.Exo150).HasColumnName("EXO150").IsRequired(false);
        builder.Property(x => x.Ferie).HasColumnName("FERIE").IsRequired(false);
        builder.Property(x => x.I130).HasColumnName("I130").IsRequired(false);
        builder.Property(x => x.I150).HasColumnName("I150").IsRequired(false);
        builder.Property(x => x.Nuit).HasColumnName("NUIT").IsRequired(false);
        builder.Property(x => x.NumSal).HasColumnName("NumSal").IsRequired(false);
        builder.Property(x => x.Retard).HasColumnName("RETARD").IsRequired(false);
    }
}
