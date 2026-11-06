using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_HEURECORRIGERConfiguration : IEntityTypeConfiguration<T_HEURECORRIGER>
{
    public void Configure(EntityTypeBuilder<T_HEURECORRIGER> builder)
    {
        builder.ToTable("T_HEURECORRIGER");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ABSENCE_AM)
            .HasColumnName("ABSENCE_AM")
            .IsRequired(false);

        builder.Property(x => x.ABSENCE_PM)
            .HasColumnName("ABSENCE_PM")
            .IsRequired(false);

        builder.Property(x => x.BDD_POINTEUSE)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BDD_SAGE)
            .HasColumnName("BDD_SAGE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.DATE_IMPORTATION_SAGE)
            .HasColumnName("DATE_IMPORTATION_SAGE")
            .IsRequired(false);

        builder.Property(x => x.DATE_POINTAGE_IN)
            .HasColumnName("DATE_POINTAGE_IN")
            .IsRequired(false);

        builder.Property(x => x.DATE_POINTAGE_OUT)
            .HasColumnName("DATE_POINTAGE_OUT")
            .IsRequired(false);

        builder.Property(x => x.FERIER)
            .HasColumnName("FERIER")
            .IsRequired(false);

        builder.Property(x => x.FERIER_AM)
            .HasColumnName("FERIER_AM")
            .IsRequired(false);

        builder.Property(x => x.FERIER_PM)
            .HasColumnName("FERIER_PM")
            .IsRequired(false);

        builder.Property(x => x.HENTREE)
            .HasColumnName("HENTREE")
            .IsRequired(false);

        builder.Property(x => x.HPE)
            .HasColumnName("HPE")
            .IsRequired(false);

        builder.Property(x => x.HPS)
            .HasColumnName("HPS")
            .IsRequired(false);

        builder.Property(x => x.HS)
            .HasColumnName("HS")
            .IsRequired(false);

        builder.Property(x => x.HSORTIE)
            .HasColumnName("HSORTIE")
            .IsRequired(false);

        builder.Property(x => x.IMPORTATION_SAGE)
            .HasColumnName("IMPORTATION_SAGE")
            .IsRequired(false);

        builder.Property(x => x.INTITULE_ABSENCE)
            .HasColumnName("INTITULE_ABSENCE")
            .IsRequired(false);

        builder.Property(x => x.MATRICULE_SAGE)
            .HasColumnName("MATRICULE_SAGE")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(x => x.M_DIMANCHE)
            .HasColumnName("M_DIMANCHE")
            .IsRequired(false);

        builder.Property(x => x.M_FERIES)
            .HasColumnName("M_FERIES")
            .IsRequired(false);

        builder.Property(x => x.M_NUIT)
            .HasColumnName("M_NUIT")
            .IsRequired(false);

        builder.Property(x => x.ORIGINE)
            .HasColumnName("ORIGINE")
            .IsRequired(false);

        builder.Property(x => x.RETARD)
            .HasColumnName("RETARD")
            .IsRequired(false);

        builder.Property(x => x.VALIDER_CORRECTION)
            .HasColumnName("VALIDER_CORRECTION")
            .IsRequired(false);

        builder.Property(x => x.VALIDER_HS)
            .HasColumnName("VALIDER_HS")
            .IsRequired(false);
    }
}
