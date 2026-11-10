using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class CardPaieConfiguration : IEntityTypeConfiguration<CardPaie>
{
    public void Configure(EntityTypeBuilder<CardPaie> builder)
    {
        builder.ToTable("T_CARDPAIE");
        builder.HasKey(x => x.Id);
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<CardPaie> builder)
    {
        builder.Property(x => x.Departement).HasColumnName("DEPARTEMENT").IsRequired(false);
        builder.Property(x => x.Origine).HasColumnName("ORIGINE").IsRequired(false);
        builder.Property(x => x.PointeuseBdd).HasColumnName("POINTEUSE_BDD").IsRequired(false);
        builder.Property(x => x.PointeuseNom).HasColumnName("POINTEUSE_NOM").IsRequired(false);
        builder.Property(x => x.PointeuseNumero).HasColumnName("POINTEUSE_NUMERO").IsRequired(false);
        builder.Property(x => x.PointeuseServeur).HasColumnName("POINTEUSE_SERVEUR").IsRequired(false);
        builder.Property(x => x.SageBdd).HasColumnName("SAGE_BDD").HasMaxLength(128).IsRequired(false);
        builder.Property(x => x.SageMatricule).HasColumnName("SAGE_MATRICULE").HasMaxLength(64).IsRequired(false);
        builder.Property(x => x.SageNom).HasColumnName("SAGE_NOM").IsRequired(false);
        builder.Property(x => x.SagePrenom).HasColumnName("SAGE_PRENOM").IsRequired(false);
        builder.Property(x => x.SageServeur).HasColumnName("SAGE_SERVEUR").IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<CardPaie> builder)
    {
        builder.Property(x => x.Date).HasColumnName("DATE").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<CardPaie> builder)
    {
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.Actif).HasColumnName("ACTIF");
    }
}
