using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Ml;

namespace SoftTime.Infrastructure.Persistence.Configurations.MlDb;

public sealed class T_ML_SALARIE_SEMAINEConfiguration : IEntityTypeConfiguration<T_ML_SALARIE_SEMAINE>
{
    public void Configure(EntityTypeBuilder<T_ML_SALARIE_SEMAINE> builder)
    {
        builder.ToTable("SalarieSemaine");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.AbsenceHeures)
            .HasColumnName("AbsenceHeures")
            .HasPrecision(8, 2);

        builder.Property(x => x.Absences4Semaines)
            .HasColumnName("Absences4Semaines");

        builder.Property(x => x.AnneeIso)
            .HasColumnName("AnneeIso");

        builder.Property(x => x.BddSage)
            .HasColumnName("BddSage")
            .HasMaxLength(128)
            .IsRequired(true);

        builder.Property(x => x.CategorieId)
            .HasColumnName("CategorieId")
            .IsRequired(false);

        builder.Property(x => x.CongeJours)
            .HasColumnName("CongeJours")
            .HasPrecision(4, 1);

        builder.Property(x => x.Departement)
            .HasColumnName("Departement")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.Dimanche)
            .HasColumnName("Dimanche")
            .HasPrecision(8, 2);

        builder.Property(x => x.Ferie)
            .HasColumnName("Ferie")
            .HasPrecision(8, 2);

        builder.Property(x => x.HS)
            .HasColumnName("HS")
            .HasPrecision(8, 2);

        builder.Property(x => x.HSValidee)
            .HasColumnName("HSValidee");

        builder.Property(x => x.HeuresPlanifiees)
            .HasColumnName("HeuresPlanifiees")
            .HasPrecision(8, 2);

        builder.Property(x => x.HeuresSemaineContrat)
            .HasColumnName("HeuresSemaineContrat")
            .HasPrecision(6, 2)
            .IsRequired(false);

        builder.Property(x => x.HeuresTravaillees)
            .HasColumnName("HeuresTravaillees")
            .HasPrecision(8, 2);

        builder.Property(x => x.HeuresTravaillees4Semaines)
            .HasColumnName("HeuresTravaillees4Semaines")
            .HasPrecision(8, 2);

        builder.Property(x => x.HsMoyenne4Semaines)
            .HasColumnName("HsMoyenne4Semaines")
            .HasPrecision(8, 2);

        builder.Property(x => x.HsMoyenne8Semaines)
            .HasColumnName("HsMoyenne8Semaines")
            .HasPrecision(8, 2);

        builder.Property(x => x.Matricule)
            .HasColumnName("Matricule")
            .HasMaxLength(64)
            .IsRequired(true);

        builder.Property(x => x.MultiPointage)
            .HasColumnName("MultiPointage");

        builder.Property(x => x.NbJoursPlanifies)
            .HasColumnName("NbJoursPlanifies");

        builder.Property(x => x.NbJoursTravailles)
            .HasColumnName("NbJoursTravailles");

        builder.Property(x => x.Nuit)
            .HasColumnName("Nuit")
            .HasPrecision(8, 2);

        builder.Property(x => x.RetardMinutes)
            .HasColumnName("RetardMinutes");

        builder.Property(x => x.Retards4Semaines)
            .HasColumnName("Retards4Semaines");

        builder.Property(x => x.SemaineDebut)
            .HasColumnName("SemaineDebut");

        builder.Property(x => x.SemaineIso)
            .HasColumnName("SemaineIso");

        builder.Property(x => x.SourceGenereeLe)
            .HasColumnName("SourceGenereeLe");

        builder.Property(x => x.VersionSchema)
            .HasColumnName("VersionSchema")
            .HasMaxLength(32)
            .IsRequired(true);

        builder.HasIndex(x => new { x.BddSage, x.SemaineDebut })
            .HasDatabaseName("IX_SalarieSemaine_BddSage_SemaineDebut");

        builder.HasIndex(x => new { x.BddSage, x.Matricule, x.SemaineDebut })
            .HasDatabaseName("IX_SalarieSemaine_BddSage_Matricule_SemaineDebut")
            .IsUnique();
    }
}
