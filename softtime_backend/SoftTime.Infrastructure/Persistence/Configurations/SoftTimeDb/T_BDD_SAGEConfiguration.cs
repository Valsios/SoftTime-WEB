using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_BDD_SAGEConfiguration : IEntityTypeConfiguration<T_BDD_SAGE>
{
    public void Configure(EntityTypeBuilder<T_BDD_SAGE> builder)
    {
        builder.ToTable("T_BDD_SAGE");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.MAP_COL_BADGE)
            .HasColumnName("MAP_COL_BADGE")
            .IsRequired(false);

        builder.Property(x => x.MAP_COL_CODE_CONSTANTE)
            .HasColumnName("MAP_COL_CODE_CONSTANTE")
            .IsRequired(false);

        builder.Property(x => x.MAP_COL_CODE_DEPARTEMENT)
            .HasColumnName("MAP_COL_CODE_DEPARTEMENT")
            .IsRequired(false);

        builder.Property(x => x.MAP_COL_DEPARTEMENT)
            .HasColumnName("MAP_COL_DEPARTEMENT")
            .IsRequired(false);

        builder.Property(x => x.MAP_COL_MATRICULE)
            .HasColumnName("MAP_COL_MATRICULE")
            .IsRequired(false);

        builder.Property(x => x.MAP_COL_NOM)
            .HasColumnName("MAP_COL_NOM")
            .IsRequired(false);

        builder.Property(x => x.MAP_COL_PRENOM)
            .HasColumnName("MAP_COL_PRENOM")
            .IsRequired(false);

        builder.Property(x => x.MAP_COL_SERVICE)
            .HasColumnName("MAP_COL_SERVICE")
            .IsRequired(false);

        builder.Property(x => x.MAP_TABLE)
            .HasColumnName("MAP_TABLE")
            .IsRequired(false);

        builder.Property(x => x.MAP_TABLE_CODE_CONSTANTE)
            .HasColumnName("MAP_TABLE_CODE_CONSTANTE")
            .IsRequired(false);

        builder.Property(x => x.NOM_BD)
            .HasColumnName("NOM_BD")
            .IsRequired(false);

        builder.Property(x => x.SERVEUR)
            .HasColumnName("SERVEUR")
            .IsRequired(false);

        builder.Property(x => x.TLOGIN)
            .HasColumnName("TLOGIN")
            .IsRequired(false);

        builder.Property(x => x.TMDP)
            .HasColumnName("TMDP")
            .IsRequired(false);

        builder.Property(x => x.TYPE_AUTH)
            .HasColumnName("TYPE_AUTH")
            .IsRequired(false);

        builder.Property(x => x.TYPE_BASE)
            .HasColumnName("TYPE_BASE")
            .IsRequired(true);
    }
}
