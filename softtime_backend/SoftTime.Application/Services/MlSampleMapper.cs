using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Models.Ml;

namespace SoftTime.Application.Services;

/// <summary>Mapping unique entre les instantanés persistés et les échantillons ML.NET.</summary>
internal static class MlSampleMapper
{
    public static MlAbsenceSample ToAbsence(T_ML_SALARIE_JOUR row) => new()
    {
        Label = row.AbsenceNonJustifiee,
        Weight = row.AbsenceNonJustifiee ? 3f : 1f,
        JourSemaine = row.JourSemaine,
        SemaineIso = row.SemaineIso,
        Mois = row.Mois,
        JourDuMois = row.JourDuMois,
        VeilleFerie = row.VeilleFerie,
        LendemainFerie = row.LendemainFerie,
        DebutMois = row.DebutMois,
        FinMois = row.FinMois,
        CategorieId = row.CategorieId ?? 0,
        Departement = row.Departement ?? string.Empty,
        HeuresSemaineContrat = (float)(row.HeuresSemaineContrat ?? 0m),
        MultiPointage = row.MultiPointage,
        AncienneteJours = row.AncienneteJours ?? 0,
        Absences7j = row.Absences7j,
        Absences30j = row.Absences30j,
        Absences90j = row.Absences90j,
        TauxAbsence30j = (float)row.TauxAbsence30j,
        Retards30j = row.Retards30j,
        JoursTravailles7j = row.JoursTravailles7j,
        JoursDepuisDerniereAbsence = row.JoursDepuisDerniereAbsence ?? 0,
        HeuresSupp4Semaines = (float)row.HeuresSupp4Semaines,
        TauxAbsenceEquipe30j = (float)row.TauxAbsenceEquipe30j,
        EffectifPrevuJour = row.EffectifPrevuJour,
        CongesPrevusJour = row.CongesPrevusJour,
        HeuresPlanifiees = (float)(row.HeuresPlanifiees ?? 0m),
        PlanningConnu = row.PlanningConnu
    };

    public static MlHsSample ToHs(T_ML_SALARIE_SEMAINE row) => new()
    {
        HsPositive = row.HS > 0m,
        HsAmount = (float)row.HS,
        SemaineIso = row.SemaineIso,
        Mois = row.SemaineDebut.Month,
        HeuresPlanifiees = (float)row.HeuresPlanifiees,
        HeuresTravaillees = (float)row.HeuresTravaillees,
        NbJoursPlanifies = row.NbJoursPlanifies,
        NbJoursTravailles = row.NbJoursTravailles,
        CongeJours = (float)row.CongeJours,
        HsMoyenne4Semaines = (float)row.HsMoyenne4Semaines,
        HsMoyenne8Semaines = (float)row.HsMoyenne8Semaines,
        Absences4Semaines = row.Absences4Semaines,
        Retards4Semaines = row.Retards4Semaines,
        HeuresTravaillees4Semaines = (float)row.HeuresTravaillees4Semaines,
        CategorieId = row.CategorieId ?? 0,
        Departement = row.Departement ?? string.Empty,
        HeuresSemaineContrat = (float)(row.HeuresSemaineContrat ?? 0m),
        MultiPointage = row.MultiPointage
    };
}
