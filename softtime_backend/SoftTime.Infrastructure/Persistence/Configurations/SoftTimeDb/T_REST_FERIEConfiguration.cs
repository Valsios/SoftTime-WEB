using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_REST_FERIEConfiguration : IEntityTypeConfiguration<T_REST_FERIE>
{
    public void Configure(EntityTypeBuilder<T_REST_FERIE> builder)
    {
        builder.ToTable("T_REST_FERIE");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.BDD_POINTEUSE)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BDD_SAGE)
            .HasColumnName("BDD_SAGE")
            .IsRequired(false);

        builder.Property(x => x.DATE_DEBUT_SEMAINE)
            .HasColumnName("DATE_DEBUT_SEMAINE")
            .IsRequired(false);

        builder.Property(x => x.ETAT)
            .HasColumnName("ETAT")
            .IsRequired(false);

        builder.Property(x => x.FERIE_DEBUT_SEMAINE)
            .HasColumnName("FERIE_DEBUT_SEMAINE")
            .IsRequired(false);

        builder.Property(x => x.MATRICULE_SAGE)
            .HasColumnName("MATRICULE_SAGE")
            .IsRequired(false);
    }
}
