using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class HistoriquePointageConfiguration : IEntityTypeConfiguration<HistoriquePointage>
{
    public void Configure(EntityTypeBuilder<HistoriquePointage> builder)
    {
        builder.ToTable("T_HISTORIQUE_POINTAGE");
        builder.HasKey(x => x.Id);
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<HistoriquePointage> builder)
    {
        builder.Property(x => x.MatriculeSage).HasColumnName("MATRICULE_SAGE").HasMaxLength(64).IsRequired(false);
        builder.Property(x => x.NomBddPointeuse).HasColumnName("NOM_BDD_POINTEUSE").IsRequired(false);
        builder.Property(x => x.NomBddSage).HasColumnName("NOM_BDD_SAGE").HasMaxLength(128).IsRequired(false);
        builder.Property(x => x.TypePointage).HasColumnName("TYPE_POINTAGE").IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<HistoriquePointage> builder)
    {
        builder.Property(x => x.DateImportation).HasColumnName("DATE_IMPORTATION").IsRequired(false);
        builder.Property(x => x.DatePointage).HasColumnName("DATE_POINTAGE").IsRequired(false);
        builder.Property(x => x.DateSuppression).HasColumnName("DATE_SUPPRESSION");
        builder.Property(x => x.HeurePointage).HasColumnName("HEURE_POINTAGE").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<HistoriquePointage> builder)
    {
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.IdUniquePointage).HasColumnName("IDUNIQUE_POINTAGE").IsRequired(false);
        builder.Property(x => x.PointageId).HasColumnName("POINTAGE_ID").IsRequired(false);
    }
}
