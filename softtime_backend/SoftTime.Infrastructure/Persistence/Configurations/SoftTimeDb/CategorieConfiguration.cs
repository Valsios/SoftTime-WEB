using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class CategorieConfiguration : IEntityTypeConfiguration<Categorie>
{
    public void Configure(EntityTypeBuilder<Categorie> builder)
    {
        builder.ToTable("T_CATEGORIE");
        builder.HasKey(x => x.IdCategorie);

        builder.Property(x => x.IdCategorie)
            .HasColumnName("IDCATEGORIE")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.BddPointeuse)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BddSage)
            .HasColumnName("BDD_SAGE")
            .IsRequired(false);

        builder.Property(x => x.HeureSemaine)
            .HasColumnName("HEURESEMAINE")
            .IsRequired(false);

        builder.Property(x => x.IdCardPaie)
            .HasColumnName("ID_CARDPAIE");

        builder.Property(x => x.Intitule)
            .HasColumnName("INTITULE")
            .IsRequired(false);

        builder.Property(x => x.Pause)
            .HasColumnName("PAUSE")
            .IsRequired(false);
    }
}
