using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_CATEGORIEConfiguration : IEntityTypeConfiguration<T_CATEGORIE>
{
    public void Configure(EntityTypeBuilder<T_CATEGORIE> builder)
    {
        builder.ToTable("T_CATEGORIE");
        builder.HasKey(x => x.IDCATEGORIE);

        builder.Property(x => x.IDCATEGORIE)
            .HasColumnName("IDCATEGORIE")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.BDD_POINTEUSE)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BDD_SAGE)
            .HasColumnName("BDD_SAGE")
            .IsRequired(false);

        builder.Property(x => x.HEURESEMAINE)
            .HasColumnName("HEURESEMAINE")
            .IsRequired(false);

        builder.Property(x => x.ID_CARDPAIE)
            .HasColumnName("ID_CARDPAIE");

        builder.Property(x => x.INTITULE)
            .HasColumnName("INTITULE")
            .IsRequired(false);

        builder.Property(x => x.PAUSE)
            .HasColumnName("PAUSE")
            .IsRequired(false);
    }
}
