using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Persistence.Configurations.SageDb;

public sealed class EvenementSalarieSageConfiguration : IEntityTypeConfiguration<EvenementSalarieSage>
{
    public void Configure(EntityTypeBuilder<EvenementSalarieSage> builder)
    {
        builder.ToTable("T_GHRSAL");
        builder.HasKey(x => x.IdGhrSal);
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<EvenementSalarieSage> builder)
    {
        builder.Property(x => x.CodeNE).HasColumnName("CodeNE").IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<EvenementSalarieSage> builder)
    {
        builder.Property(x => x.PeriodeDebut).HasColumnName("PeriodeDebut").IsRequired(false);
        builder.Property(x => x.PeriodeFin).HasColumnName("PeriodeFin").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<EvenementSalarieSage> builder)
    {
        builder.Property(x => x.IdGhrSal).HasColumnName("IdGhrSal").ValueGeneratedOnAdd();
        builder.Property(x => x.ApresMidi).HasColumnName("ApresMidi").IsRequired(false);
        builder.Property(x => x.Matin).HasColumnName("Matin").IsRequired(false);
        builder.Property(x => x.NombreParJour).HasColumnName("NombreParJour").IsRequired(false);
        builder.Property(x => x.NumSalarie).HasColumnName("NumSalarie");
        builder.Property(x => x.Valeur).HasColumnName("Valeur").IsRequired(false);
    }
}
