using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

/// <summary>
/// Schéma créé par migrations/versions/009_instantanes_paie.sql. Les déclencheurs d'immuabilité sont déclarés à EF Core :
/// sans cela, l'insertion (OUTPUT sans INTO) serait refusée par SQL Server sur une table qui a des déclencheurs.
/// </summary>
public sealed class PaieInstantaneConfiguration : IEntityTypeConfiguration<PaieInstantane>
{
    public void Configure(EntityTypeBuilder<PaieInstantane> builder)
    {
        builder.ToTable("T_PAIE_INSTANTANE", t => t.HasTrigger("TR_T_PAIE_INSTANTANE_IMMUABLE"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.EnvoiId).HasColumnName("ENVOI_ID");
        builder.Property(x => x.BddSage).HasColumnName("BDD_SAGE").HasMaxLength(128).IsRequired();
        builder.Property(x => x.PeriodeDebut).HasColumnName("PERIODE_DEBUT").HasColumnType("date");
        builder.Property(x => x.PeriodeFin).HasColumnName("PERIODE_FIN").HasColumnType("date");
        builder.Property(x => x.TypeBase).HasColumnName("TYPE_BASE").HasMaxLength(16).IsRequired();
        builder.Property(x => x.EnvoyeLe).HasColumnName("ENVOYE_LE").HasColumnType("datetime2");
        builder.Property(x => x.EnvoyePar).HasColumnName("ENVOYE_PAR").HasMaxLength(256);
        builder.Property(x => x.Departement).HasColumnName("DEPARTEMENT").HasMaxLength(128);
        builder.Property(x => x.MatriculeDe).HasColumnName("MATRICULE_DE").HasMaxLength(64);
        builder.Property(x => x.MatriculeA).HasColumnName("MATRICULE_A").HasMaxLength(64);
        builder.Property(x => x.AvecFiltre).HasColumnName("AVEC_FILTRE");
        builder.Property(x => x.RemiseAZero).HasColumnName("REMISE_A_ZERO");
        builder.Property(x => x.HeuresParJour).HasColumnName("HEURES_PAR_JOUR").HasColumnType("decimal(9,4)");
        builder.Property(x => x.VersionFormat).HasColumnName("VERSION_FORMAT");
        builder.Property(x => x.Empreinte).HasColumnName("EMPREINTE").HasColumnType("char(64)").IsRequired();
        builder.Property(x => x.NbLignes).HasColumnName("NB_LIGNES");
        builder.Property(x => x.NbSansLigne).HasColumnName("NB_SANS_LIGNE");
        builder.Property(x => x.NbNonRemis).HasColumnName("NB_NON_REMIS");
        builder.Property(x => x.CreeLe).HasColumnName("CREE_LE").HasColumnType("datetime2");
        builder.HasIndex(x => x.EnvoiId).IsUnique().HasDatabaseName("UX_T_PAIE_INSTANTANE_ENVOI");
        builder.HasIndex(x => new { x.BddSage, x.PeriodeDebut, x.PeriodeFin }).HasDatabaseName("IX_T_PAIE_INSTANTANE_BDD_PERIODE");
    }
}

/// <summary>Schéma créé par migrations/versions/009_instantanes_paie.sql.</summary>
public sealed class PaieInstantaneLigneConfiguration : IEntityTypeConfiguration<PaieInstantaneLigne>
{
    public void Configure(EntityTypeBuilder<PaieInstantaneLigne> builder)
    {
        builder.ToTable("T_PAIE_INSTANTANE_LIGNE", t => t.HasTrigger("TR_T_PAIE_INSTANTANE_LIGNE_IMMUABLE"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.InstantaneId).HasColumnName("INSTANTANE_ID");
        builder.Property(x => x.Matricule).HasColumnName("MATRICULE").HasMaxLength(64).IsRequired();
        builder.Property(x => x.NumSal).HasColumnName("NUM_SAL");
        builder.Property(x => x.Variable).HasColumnName("VARIABLE").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Destination).HasColumnName("DESTINATION").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Unite).HasColumnName("UNITE").HasMaxLength(1).IsRequired();
        builder.Property(x => x.Valeur).HasColumnName("VALEUR").HasColumnType("decimal(18,4)");
        builder.Property(x => x.RemiseAZero).HasColumnName("REMISE_A_ZERO");
        builder.HasIndex(x => x.InstantaneId).HasDatabaseName("IX_T_PAIE_INSTANTANE_LIGNE_INSTANTANE");
    }
}

/// <summary>Schéma créé par migrations/versions/010_cloture_paie.sql (déclencheur d'immuabilité déclaré à EF Core).</summary>
public sealed class PaieClotureConfiguration : IEntityTypeConfiguration<PaieCloture>
{
    public void Configure(EntityTypeBuilder<PaieCloture> builder)
    {
        builder.ToTable("T_PAIE_CLOTURE", t => t.HasTrigger("TR_T_PAIE_CLOTURE_IMMUABLE"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.BddSage).HasColumnName("BDD_SAGE").HasMaxLength(128).IsRequired();
        builder.Property(x => x.PeriodeDebut).HasColumnName("PERIODE_DEBUT").HasColumnType("date");
        builder.Property(x => x.PeriodeFin).HasColumnName("PERIODE_FIN").HasColumnType("date");
        builder.Property(x => x.ClotureLe).HasColumnName("CLOTURE_LE").HasColumnType("datetime2");
        builder.Property(x => x.CloturePar).HasColumnName("CLOTURE_PAR").HasMaxLength(256).IsRequired();
        builder.Property(x => x.EnvoiId).HasColumnName("ENVOI_ID");
        builder.Property(x => x.InstantaneId).HasColumnName("INSTANTANE_ID");
        builder.Property(x => x.Empreinte).HasColumnName("EMPREINTE").HasColumnType("char(64)").IsRequired();
        builder.Property(x => x.StatutControle).HasColumnName("STATUT_CONTROLE").HasMaxLength(16).IsRequired();
        builder.Property(x => x.ResumeControle).HasColumnName("RESUME_CONTROLE").HasMaxLength(1000).IsRequired();
        builder.HasIndex(x => new { x.BddSage, x.PeriodeDebut, x.PeriodeFin }).IsUnique().HasDatabaseName("UX_T_PAIE_CLOTURE_BDD_PERIODE");
    }
}
