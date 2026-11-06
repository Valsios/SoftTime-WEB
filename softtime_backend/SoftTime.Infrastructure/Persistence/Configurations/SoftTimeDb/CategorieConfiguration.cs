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
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<Categorie> builder)
    {
        builder.Property(x => x.BddPointeuse).HasColumnName("BDD_POINTEUSE").IsRequired(false);
        builder.Property(x => x.BddSage).HasColumnName("BDD_SAGE").IsRequired(false);
        builder.Property(x => x.Intitule).HasColumnName("INTITULE").IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<Categorie> builder)
    {
        builder.Property(x => x.Pause).HasColumnName("PAUSE").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<Categorie> builder)
    {
        builder.Property(x => x.IdCategorie).HasColumnName("IDCATEGORIE").ValueGeneratedOnAdd();
        builder.Property(x => x.HeureSemaine).HasColumnName("HEURESEMAINE").IsRequired(false);
        builder.Property(x => x.IdCardPaie).HasColumnName("ID_CARDPAIE");
    }
}
