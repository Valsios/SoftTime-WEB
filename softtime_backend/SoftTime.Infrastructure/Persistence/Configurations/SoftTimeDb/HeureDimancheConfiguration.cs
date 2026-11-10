using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class HeureDimancheConfiguration : IEntityTypeConfiguration<HeureDimanche>
{
    public void Configure(EntityTypeBuilder<HeureDimanche> builder)
    {
        builder.ToTable("T_HEUREDIMANCHE");
        builder.HasKey(x => x.Id);
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<HeureDimanche> builder)
    {
        builder.Property(x => x.BddPointeuse).HasColumnName("BDD_POINTEUSE").IsRequired(false);
        builder.Property(x => x.BddSage).HasColumnName("BDD_SAGE").HasMaxLength(128).IsRequired(false);
        builder.Property(x => x.MatriculeSage).HasColumnName("MATRICULE_SAGE").HasMaxLength(64).IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<HeureDimanche> builder)
    {
        builder.Property(x => x.DateDim).HasColumnName("Date_Dim").IsRequired(false);
        builder.Property(x => x.HeureDebut).HasColumnName("HeureDebut").IsRequired(false);
        builder.Property(x => x.HeureFin).HasColumnName("HeureFin").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<HeureDimanche> builder)
    {
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.Valeur).HasColumnName("Valeur").IsRequired(false);
    }
}
