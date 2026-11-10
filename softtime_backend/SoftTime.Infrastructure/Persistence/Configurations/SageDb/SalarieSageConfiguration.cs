using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Persistence.Configurations.SageDb;

public sealed class SalarieSageConfiguration : IEntityTypeConfiguration<SalarieSage>
{
    public void Configure(EntityTypeBuilder<SalarieSage> builder)
    {
        builder.ToTable("T_SAL");
        builder.HasKey(x => x.SaCompteurNumero);
        ConfigureTextColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<SalarieSage> builder)
    {
        builder.Property(x => x.MatriculeSalarie).HasColumnName("MatriculeSalarie").IsRequired(false);
        builder.Property(x => x.Nom).HasColumnName("Nom").IsRequired(false);
        builder.Property(x => x.NumeroDeBadge).HasColumnName("NumeroDeBadge").IsRequired(false);
        builder.Property(x => x.Prenom).HasColumnName("Prenom").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<SalarieSage> builder)
    {
        builder.Property(x => x.SaCompteurNumero).HasColumnName("SA_CompteurNumero").ValueGeneratedOnAdd();
        builder.Property(x => x.Civilite).HasColumnName("Civilite").IsRequired(false);
        builder.Property(x => x.SalarieDesactive).HasColumnName("SalarieDesactive").IsRequired(false);
    }
}
