using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Persistence.Configurations.SageDb;

public sealed class HistoriqueAffectationSageConfiguration : IEntityTypeConfiguration<HistoriqueAffectationSage>
{
    public void Configure(EntityTypeBuilder<HistoriqueAffectationSage> builder)
    {
        builder.ToTable("T_HST_AFFECTATION");
        builder.HasKey(x => x.IdHstAffectation);
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<HistoriqueAffectationSage> builder)
    {
        builder.Property(x => x.Categorie).HasColumnName("Categorie").IsRequired(false);
        builder.Property(x => x.Departement).HasColumnName("Departement").IsRequired(false);
        builder.Property(x => x.Service).HasColumnName("Service").IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<HistoriqueAffectationSage> builder)
    {
        builder.Property(x => x.DateDebut).HasColumnName("DateDebut").IsRequired(false);
        builder.Property(x => x.DateSortiePoste).HasColumnName("DateSortiePoste").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<HistoriqueAffectationSage> builder)
    {
        builder.Property(x => x.IdHstAffectation).HasColumnName("IdHstAffectation").ValueGeneratedOnAdd();
        builder.Property(x => x.NumSalarie).HasColumnName("NumSalarie");
    }
}
