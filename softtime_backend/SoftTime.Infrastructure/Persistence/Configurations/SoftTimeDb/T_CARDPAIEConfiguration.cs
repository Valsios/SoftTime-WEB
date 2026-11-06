using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_CARDPAIEConfiguration : IEntityTypeConfiguration<T_CARDPAIE>
{
    public void Configure(EntityTypeBuilder<T_CARDPAIE> builder)
    {
        builder.ToTable("T_CARDPAIE");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ACTIF)
            .HasColumnName("ACTIF");

        builder.Property(x => x.DATE)
            .HasColumnName("DATE")
            .IsRequired(false);

        builder.Property(x => x.DEPARTEMENT)
            .HasColumnName("DEPARTEMENT")
            .IsRequired(false);

        builder.Property(x => x.ORIGINE)
            .HasColumnName("ORIGINE")
            .IsRequired(true);

        builder.Property(x => x.POINTEUSE_BDD)
            .HasColumnName("POINTEUSE_BDD")
            .IsRequired(false);

        builder.Property(x => x.POINTEUSE_NOM)
            .HasColumnName("POINTEUSE_NOM")
            .IsRequired(false);

        builder.Property(x => x.POINTEUSE_NUMERO)
            .HasColumnName("POINTEUSE_NUMERO")
            .IsRequired(false);

        builder.Property(x => x.POINTEUSE_SERVEUR)
            .HasColumnName("POINTEUSE_SERVEUR")
            .IsRequired(false);

        builder.Property(x => x.SAGE_BDD)
            .HasColumnName("SAGE_BDD")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.SAGE_MATRICULE)
            .HasColumnName("SAGE_MATRICULE")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(x => x.SAGE_NOM)
            .HasColumnName("SAGE_NOM")
            .IsRequired(false);

        builder.Property(x => x.SAGE_PRENOM)
            .HasColumnName("SAGE_PRENOM")
            .IsRequired(false);

        builder.Property(x => x.SAGE_SERVEUR)
            .HasColumnName("SAGE_SERVEUR")
            .IsRequired(false);
    }
}
