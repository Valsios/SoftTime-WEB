using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class HistoriqueHsConfiguration : IEntityTypeConfiguration<HistoriqueHs>
{
    public void Configure(EntityTypeBuilder<HistoriqueHs> builder)
    {
        builder.ToTable("T_HISTORIQUE_HS");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Absence)
            .HasColumnName("ABSENCE")
            .IsRequired(false);

        builder.Property(x => x.BddPointeuse)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BddSage)
            .HasColumnName("BDD_SAGE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.DateSuppression)
            .HasColumnName("DATE_SUPPRESSION");

        builder.Property(x => x.Dim)
            .HasColumnName("DIM")
            .IsRequired(false);

        builder.Property(x => x.Exo130)
            .HasColumnName("EXO130")
            .IsRequired(false);

        builder.Property(x => x.Exo150)
            .HasColumnName("EXO150")
            .IsRequired(false);

        builder.Property(x => x.Ferie)
            .HasColumnName("FERIE")
            .IsRequired(false);

        builder.Property(x => x.HsId)
            .HasColumnName("HS_ID")
            .IsRequired(false);

        builder.Property(x => x.I130)
            .HasColumnName("I130")
            .IsRequired(false);

        builder.Property(x => x.I150)
            .HasColumnName("I150")
            .IsRequired(false);

        builder.Property(x => x.Matricule)
            .HasColumnName("MATRICULE")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(x => x.Nuit)
            .HasColumnName("NUIT")
            .IsRequired(false);

        builder.Property(x => x.NumSal)
            .HasColumnName("NUM_SAL")
            .IsRequired(false);

        builder.Property(x => x.PeriodeDebut)
            .HasColumnName("PERIODE_DEBUT")
            .IsRequired(false);

        builder.Property(x => x.PeriodeFin)
            .HasColumnName("PERIODE_FIN")
            .IsRequired(false);

        builder.Property(x => x.Retard)
            .HasColumnName("RETARD")
            .IsRequired(false);
    }
}
