using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_HSSemaineConfiguration : IEntityTypeConfiguration<T_HSSemaine>
{
    public void Configure(EntityTypeBuilder<T_HSSemaine> builder)
    {
        builder.ToTable("T_HSSemaine");
        builder.HasKey(x => x.OID);

        builder.Property(x => x.OID)
            .HasColumnName("OID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.BDD_POINTEUSE)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BDD_SAGE)
            .HasColumnName("BDD_SAGE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.HS)
            .HasColumnName("HS")
            .IsRequired(false);

        builder.Property(x => x.HS1)
            .HasColumnName("HS1")
            .IsRequired(false);

        builder.Property(x => x.HS2)
            .HasColumnName("HS2")
            .IsRequired(false);

        builder.Property(x => x.Matricule)
            .HasColumnName("Matricule")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(x => x.PeriodeDebut)
            .HasColumnName("PeriodeDebut")
            .IsRequired(false);

        builder.Property(x => x.PeriodeFin)
            .HasColumnName("PeriodeFin")
            .IsRequired(false);

        builder.Property(x => x.SalNum)
            .HasColumnName("SalNum")
            .IsRequired(false);

        builder.Property(x => x.Semaine)
            .HasColumnName("Semaine")
            .IsRequired(false);

        builder.Property(x => x.TotalHeure)
            .HasColumnName("TotalHeure")
            .IsRequired(false);
    }
}
