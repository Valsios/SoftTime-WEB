using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Persistence.Configurations.SageDb;

public sealed class T_SALConfiguration : IEntityTypeConfiguration<T_SAL>
{
    public void Configure(EntityTypeBuilder<T_SAL> builder)
    {
        builder.ToTable("T_SAL");
        builder.HasKey(x => x.SA_CompteurNumero);

        builder.Property(x => x.SA_CompteurNumero)
            .HasColumnName("SA_CompteurNumero")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Civilite)
            .HasColumnName("Civilite")
            .IsRequired(false);

        builder.Property(x => x.MatriculeSalarie)
            .HasColumnName("MatriculeSalarie")
            .IsRequired(true);

        builder.Property(x => x.Nom)
            .HasColumnName("Nom")
            .IsRequired(false);

        builder.Property(x => x.NumeroDeBadge)
            .HasColumnName("NumeroDeBadge")
            .IsRequired(false);

        builder.Property(x => x.Prenom)
            .HasColumnName("Prenom")
            .IsRequired(false);

        builder.Property(x => x.SalarieDesactive)
            .HasColumnName("SalarieDesactive")
            .IsRequired(false);
    }
}
