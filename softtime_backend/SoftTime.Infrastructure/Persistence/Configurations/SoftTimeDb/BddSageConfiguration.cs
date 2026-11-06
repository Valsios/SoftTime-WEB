using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class BddSageConfiguration : IEntityTypeConfiguration<BddSage>
{
    public void Configure(EntityTypeBuilder<BddSage> builder)
    {
        builder.ToTable("T_BDD_SAGE");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.MapColBadge)
            .HasColumnName("MAP_COL_BADGE")
            .IsRequired(false);

        builder.Property(x => x.MapColCodeConstante)
            .HasColumnName("MAP_COL_CODE_CONSTANTE")
            .IsRequired(false);

        builder.Property(x => x.MapColCodeDepartement)
            .HasColumnName("MAP_COL_CODE_DEPARTEMENT")
            .IsRequired(false);

        builder.Property(x => x.MapColDepartement)
            .HasColumnName("MAP_COL_DEPARTEMENT")
            .IsRequired(false);

        builder.Property(x => x.MapColMatricule)
            .HasColumnName("MAP_COL_MATRICULE")
            .IsRequired(false);

        builder.Property(x => x.MapColNom)
            .HasColumnName("MAP_COL_NOM")
            .IsRequired(false);

        builder.Property(x => x.MapColPrenom)
            .HasColumnName("MAP_COL_PRENOM")
            .IsRequired(false);

        builder.Property(x => x.MapColService)
            .HasColumnName("MAP_COL_SERVICE")
            .IsRequired(false);

        builder.Property(x => x.MapTable)
            .HasColumnName("MAP_TABLE")
            .IsRequired(false);

        builder.Property(x => x.MapTableCodeConstante)
            .HasColumnName("MAP_TABLE_CODE_CONSTANTE")
            .IsRequired(false);

        builder.Property(x => x.NomBd)
            .HasColumnName("NOM_BD")
            .IsRequired(false);

        builder.Property(x => x.Serveur)
            .HasColumnName("SERVEUR")
            .IsRequired(false);

        builder.Property(x => x.TLogin)
            .HasColumnName("TLOGIN")
            .IsRequired(false);

        builder.Property(x => x.TMdp)
            .HasColumnName("TMDP")
            .IsRequired(false);

        builder.Property(x => x.TypeAuth)
            .HasColumnName("TYPE_AUTH")
            .IsRequired(false);

        builder.Property(x => x.TypeBase)
            .HasColumnName("TYPE_BASE")
            .IsRequired(false);
    }
}
