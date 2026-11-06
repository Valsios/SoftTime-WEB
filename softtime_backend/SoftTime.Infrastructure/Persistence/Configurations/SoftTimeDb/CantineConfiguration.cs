using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class CantineConfiguration : IEntityTypeConfiguration<Cantine>
{
    public void Configure(EntityTypeBuilder<Cantine> builder)
    {
        builder.ToTable("T_CANTINE");
        builder.HasKey(x => x.IdCantine);
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<Cantine> builder)
    {
        builder.Property(x => x.MatriculeSage).HasColumnName("MATRICULE_SAGE").HasMaxLength(64).IsRequired(false);
        builder.Property(x => x.NomBddPointeuse).HasColumnName("NOM_BDD_POINTEUSE").IsRequired(false);
        builder.Property(x => x.NomBddSage).HasColumnName("NOM_BDD_SAGE").HasMaxLength(128).IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<Cantine> builder)
    {
        builder.Property(x => x.DateDeb).HasColumnName("DATEDEB").IsRequired(false);
        builder.Property(x => x.DateFin).HasColumnName("DATEFIN").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<Cantine> builder)
    {
        builder.Property(x => x.IdCantine).HasColumnName("IDCANTINE").ValueGeneratedOnAdd();
        builder.Property(x => x.TotalCantine).HasColumnName("TOTAL_CANTINE").IsRequired(false);
    }
}
