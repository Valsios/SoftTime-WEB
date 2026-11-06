using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class HeureCorrigeeConfiguration : IEntityTypeConfiguration<HeureCorrigee>
{
    public void Configure(EntityTypeBuilder<HeureCorrigee> builder)
    {
        builder.ToTable("T_HEURECORRIGER");
        builder.HasKey(x => x.Id);
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<HeureCorrigee> builder)
    {
        builder.Property(x => x.BddPointeuse).HasColumnName("BDD_POINTEUSE").IsRequired(false);
        builder.Property(x => x.BddSage).HasColumnName("BDD_SAGE").HasMaxLength(128).IsRequired(false);
        builder.Property(x => x.IntituleAbsence).HasColumnName("INTITULE_ABSENCE").IsRequired(false);
        builder.Property(x => x.MatriculeSage).HasColumnName("MATRICULE_SAGE").HasMaxLength(64).IsRequired(false);
        builder.Property(x => x.Origine).HasColumnName("ORIGINE").IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<HeureCorrigee> builder)
    {
        builder.Property(x => x.DateImportationSage).HasColumnName("DATE_IMPORTATION_SAGE").IsRequired(false);
        builder.Property(x => x.DatePointageIn).HasColumnName("DATE_POINTAGE_IN").IsRequired(false);
        builder.Property(x => x.DatePointageOut).HasColumnName("DATE_POINTAGE_OUT").IsRequired(false);
        builder.Property(x => x.HEntree).HasColumnName("HENTREE").IsRequired(false);
        builder.Property(x => x.HSortie).HasColumnName("HSORTIE").IsRequired(false);
        builder.Property(x => x.Hpe).HasColumnName("HPE").IsRequired(false);
        builder.Property(x => x.Hps).HasColumnName("HPS").IsRequired(false);
        builder.Property(x => x.Retard).HasColumnName("RETARD").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<HeureCorrigee> builder)
    {
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.AbsenceAm).HasColumnName("ABSENCE_AM").IsRequired(false);
        builder.Property(x => x.AbsencePm).HasColumnName("ABSENCE_PM").IsRequired(false);
        builder.Property(x => x.Ferier).HasColumnName("FERIER").IsRequired(false);
        builder.Property(x => x.FerierAm).HasColumnName("FERIER_AM").IsRequired(false);
        builder.Property(x => x.FerierPm).HasColumnName("FERIER_PM").IsRequired(false);
        builder.Property(x => x.Hs).HasColumnName("HS").IsRequired(false);
        builder.Property(x => x.ImportationSage).HasColumnName("IMPORTATION_SAGE").IsRequired(false);
        builder.Property(x => x.MDimanche).HasColumnName("M_DIMANCHE").IsRequired(false);
        builder.Property(x => x.MFeries).HasColumnName("M_FERIES").IsRequired(false);
        builder.Property(x => x.MNuit).HasColumnName("M_NUIT").IsRequired(false);
        builder.Property(x => x.ValiderCorrection).HasColumnName("VALIDER_CORRECTION").IsRequired(false);
        builder.Property(x => x.ValiderHs).HasColumnName("VALIDER_HS").IsRequired(false);
    }
}
