using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Ml;

namespace SoftTime.Infrastructure.Persistence.Configurations.MlDb;

public sealed class MlSalarieJourConfiguration : IEntityTypeConfiguration<MlSalarieJour>
{
    public void Configure(EntityTypeBuilder<MlSalarieJour> builder)
    {
        builder.ToTable("SalarieJour");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.AbsenceDeclaree)
            .HasColumnName("AbsenceDeclaree");

        builder.Property(x => x.AbsenceHeures)
            .HasColumnName("AbsenceHeures")
            .HasPrecision(6, 2)
            .IsRequired(false);

        builder.Property(x => x.AbsenceNonJustifiee)
            .HasColumnName("AbsenceNonJustifiee");

        builder.Property(x => x.Absences30j)
            .HasColumnName("Absences30j");

        builder.Property(x => x.Absences7j)
            .HasColumnName("Absences7j");

        builder.Property(x => x.Absences90j)
            .HasColumnName("Absences90j");

        builder.Property(x => x.AncienneteJours)
            .HasColumnName("AncienneteJours")
            .IsRequired(false);

        builder.Property(x => x.AnneeIso)
            .HasColumnName("AnneeIso");

        builder.Property(x => x.BddSage)
            .HasColumnName("BddSage")
            .HasMaxLength(128)
            .IsRequired(true);

        builder.Property(x => x.CategorieId)
            .HasColumnName("CategorieId")
            .IsRequired(false);

        builder.Property(x => x.CongesPrevusJour)
            .HasColumnName("CongesPrevusJour");

        builder.Property(x => x.Date)
            .HasColumnName("Date");

        builder.Property(x => x.DebutMois)
            .HasColumnName("DebutMois");

        builder.Property(x => x.Departement)
            .HasColumnName("Departement")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.EffectifPrevuJour)
            .HasColumnName("EffectifPrevuJour");

        builder.Property(x => x.EstConge)
            .HasColumnName("EstConge");

        builder.Property(x => x.EstFerie)
            .HasColumnName("EstFerie");

        builder.Property(x => x.EstJourPlanifie)
            .HasColumnName("EstJourPlanifie");

        builder.Property(x => x.EstOff)
            .HasColumnName("EstOff");

        builder.Property(x => x.EstWeekend)
            .HasColumnName("EstWeekend");

        builder.Property(x => x.FinMois)
            .HasColumnName("FinMois");

        builder.Property(x => x.HeuresPlanifiees)
            .HasColumnName("HeuresPlanifiees")
            .HasPrecision(6, 2)
            .IsRequired(false);

        builder.Property(x => x.HeuresSemaineContrat)
            .HasColumnName("HeuresSemaineContrat")
            .HasPrecision(6, 2)
            .IsRequired(false);

        builder.Property(x => x.HeuresSupp4Semaines)
            .HasColumnName("HeuresSupp4Semaines")
            .HasPrecision(8, 2);

        builder.Property(x => x.HeuresTravaillees)
            .HasColumnName("HeuresTravaillees")
            .HasPrecision(6, 2)
            .IsRequired(false);

        builder.Property(x => x.JourDuMois)
            .HasColumnName("JourDuMois");

        builder.Property(x => x.JourSemaine)
            .HasColumnName("JourSemaine");

        builder.Property(x => x.JoursDepuisDerniereAbsence)
            .HasColumnName("JoursDepuisDerniereAbsence")
            .IsRequired(false);

        builder.Property(x => x.JoursTravailles7j)
            .HasColumnName("JoursTravailles7j");

        builder.Property(x => x.LendemainFerie)
            .HasColumnName("LendemainFerie");

        builder.Property(x => x.Matricule)
            .HasColumnName("Matricule")
            .HasMaxLength(64)
            .IsRequired(true);

        builder.Property(x => x.Mois)
            .HasColumnName("Mois");

        builder.Property(x => x.MultiPointage)
            .HasColumnName("MultiPointage");

        builder.Property(x => x.PlanningConnu)
            .HasColumnName("PlanningConnu");

        builder.Property(x => x.RetardMinutes)
            .HasColumnName("RetardMinutes")
            .IsRequired(false);

        builder.Property(x => x.Retards30j)
            .HasColumnName("Retards30j");

        builder.Property(x => x.SemaineIso)
            .HasColumnName("SemaineIso");

        builder.Property(x => x.SourceGenereeLe)
            .HasColumnName("SourceGenereeLe");

        builder.Property(x => x.TauxAbsence30j)
            .HasColumnName("TauxAbsence30j");

        builder.Property(x => x.TauxAbsenceEquipe30j)
            .HasColumnName("TauxAbsenceEquipe30j");

        builder.Property(x => x.VeilleFerie)
            .HasColumnName("VeilleFerie");

        builder.Property(x => x.VersionSchema)
            .HasColumnName("VersionSchema")
            .HasMaxLength(32)
            .IsRequired(true);

        builder.HasIndex(x => new { x.BddSage, x.Date })
            .HasDatabaseName("IX_SalarieJour_BddSage_Date");

        builder.HasIndex(x => new { x.BddSage, x.Matricule, x.Date })
            .HasDatabaseName("IX_SalarieJour_BddSage_Matricule_Date")
            .IsUnique();
    }
}
