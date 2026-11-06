using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Persistence.Configurations.SageDb;

public sealed class T_HST_AFFECTATIONConfiguration : IEntityTypeConfiguration<T_HST_AFFECTATION>
{
    public void Configure(EntityTypeBuilder<T_HST_AFFECTATION> builder)
    {
        builder.ToTable("T_HST_AFFECTATION");
        builder.HasKey(x => x.IdHstAffectation);

        builder.Property(x => x.IdHstAffectation)
            .HasColumnName("IdHstAffectation")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Categorie)
            .HasColumnName("Categorie")
            .IsRequired(false);

        builder.Property(x => x.DateDebut)
            .HasColumnName("DateDebut")
            .IsRequired(false);

        builder.Property(x => x.DateSortiePoste)
            .HasColumnName("DateSortiePoste")
            .IsRequired(false);

        builder.Property(x => x.Departement)
            .HasColumnName("Departement")
            .IsRequired(false);

        builder.Property(x => x.NumSalarie)
            .HasColumnName("NumSalarie");

        builder.Property(x => x.Service)
            .HasColumnName("Service")
            .IsRequired(false);
    }
}
