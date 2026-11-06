using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class FraisConfiguration : IEntityTypeConfiguration<Frais>
{
    public void Configure(EntityTypeBuilder<Frais> builder)
    {
        builder.ToTable("T_FRAIS");
        builder.HasKey(x => x.IdPointage);
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<Frais> builder)
    {
        builder.Property(x => x.MatriculeSage).HasColumnName("MATRICULE_SAGE").HasMaxLength(64).IsRequired(false);
        builder.Property(x => x.NomBddPointeuse).HasColumnName("NOM_BDD_POINTEUSE").IsRequired(false);
        builder.Property(x => x.NomBddSage).HasColumnName("NOM_BDD_SAGE").HasMaxLength(128).IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<Frais> builder)
    {
        builder.Property(x => x.DateDeb).HasColumnName("DATEDEB").IsRequired(false);
        builder.Property(x => x.DateFin).HasColumnName("DATEFIN").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<Frais> builder)
    {
        builder.Property(x => x.IdPointage).HasColumnName("IDPOINTAGE").ValueGeneratedOnAdd();
        builder.Property(x => x.TotalPoint).HasColumnName("TOTAL_POINT").IsRequired(false);
    }
}
