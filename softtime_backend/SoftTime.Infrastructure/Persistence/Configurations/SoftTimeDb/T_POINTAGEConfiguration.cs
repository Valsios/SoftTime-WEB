using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_POINTAGEConfiguration : IEntityTypeConfiguration<T_POINTAGE>
{
    public void Configure(EntityTypeBuilder<T_POINTAGE> builder)
    {
        builder.ToTable("T_POINTAGE");
        builder.HasKey(x => x.IDPOINTAGE);

        builder.Property(x => x.IDPOINTAGE)
            .HasColumnName("IDPOINTAGE")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.DATE_IMPORTATION)
            .HasColumnName("DATE_IMPORTATION")
            .IsRequired(false);

        builder.Property(x => x.DATE_POINTAGE)
            .HasColumnName("DATE_POINTAGE")
            .IsRequired(false);

        builder.Property(x => x.HEURE_POINTAGE)
            .HasColumnName("HEURE_POINTAGE")
            .IsRequired(false);

        builder.Property(x => x.IDUNIQUE_POINTAGE)
            .HasColumnName("IDUNIQUE_POINTAGE")
            .IsRequired(false);

        builder.Property(x => x.MATRICULE_SAGE)
            .HasColumnName("MATRICULE_SAGE")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(x => x.NOM_BDD_POINTEUSE)
            .HasColumnName("NOM_BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.NOM_BDD_SAGE)
            .HasColumnName("NOM_BDD_SAGE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.TYPE_POINTAGE)
            .HasColumnName("TYPE_POINTAGE")
            .IsRequired(false);
    }
}
