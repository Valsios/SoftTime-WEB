using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_HSExoImpConfiguration : IEntityTypeConfiguration<T_HSExoImp>
{
    public void Configure(EntityTypeBuilder<T_HSExoImp> builder)
    {
        builder.ToTable("T_HSExoImp");
        builder.HasKey(x => x.OID);

        builder.Property(x => x.OID)
            .HasColumnName("OID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ABSENCE)
            .HasColumnName("ABSENCE")
            .IsRequired(false);

        builder.Property(x => x.BDD_POINTEUSE)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BDD_SAGE)
            .HasColumnName("BDD_SAGE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.DIM)
            .HasColumnName("DIM")
            .IsRequired(false);

        builder.Property(x => x.EXO130)
            .HasColumnName("EXO130")
            .IsRequired(false);

        builder.Property(x => x.EXO150)
            .HasColumnName("EXO150")
            .IsRequired(false);

        builder.Property(x => x.FERIE)
            .HasColumnName("FERIE")
            .IsRequired(false);

        builder.Property(x => x.I130)
            .HasColumnName("I130")
            .IsRequired(false);

        builder.Property(x => x.I150)
            .HasColumnName("I150")
            .IsRequired(false);

        builder.Property(x => x.Matricule)
            .HasColumnName("Matricule")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(x => x.NUIT)
            .HasColumnName("NUIT")
            .IsRequired(false);

        builder.Property(x => x.NumSal)
            .HasColumnName("NumSal")
            .IsRequired(false);

        builder.Property(x => x.PeriodeDebut)
            .HasColumnName("PeriodeDebut")
            .IsRequired(false);

        builder.Property(x => x.PeriodeFin)
            .HasColumnName("PeriodeFin")
            .IsRequired(false);

        builder.Property(x => x.RETARD)
            .HasColumnName("RETARD")
            .IsRequired(false);
    }
}
