using System.Security.Cryptography;
using System.Text;
using SoftTime.Domain.Models.Ml;

namespace SoftTime.Infrastructure.MachineLearning;

/// <summary>Contrat ML.NET pour l'absentéisme (grain salarié × jour).</summary>
internal sealed class MlAbsenceData
{
    public bool Label { get; set; }
    public float Weight { get; set; } = 1f;

    public float JourSemaine { get; set; }
    public float SemaineIso { get; set; }
    public float Mois { get; set; }
    public float JourDuMois { get; set; }
    public float VeilleFerie { get; set; }
    public float LendemainFerie { get; set; }
    public float DebutMois { get; set; }
    public float FinMois { get; set; }

    public float CategorieId { get; set; }
    public string Departement { get; set; } = string.Empty;
    public float HeuresSemaineContrat { get; set; }
    public float MultiPointage { get; set; }
    public float AncienneteJours { get; set; }

    public float Absences7j { get; set; }
    public float Absences30j { get; set; }
    public float Absences90j { get; set; }
    public float TauxAbsence30j { get; set; }
    public float Retards30j { get; set; }
    public float JoursTravailles7j { get; set; }
    public float JoursDepuisDerniereAbsence { get; set; }
    public float HeuresSupp4Semaines { get; set; }

    public float TauxAbsenceEquipe30j { get; set; }
    public float EffectifPrevuJour { get; set; }
    public float CongesPrevusJour { get; set; }

    public float HeuresPlanifiees { get; set; }
    public float PlanningConnu { get; set; }
}

/// <summary>Contrat ML.NET pour les heures supplémentaires (grain salarié × semaine).</summary>
internal sealed class MlHsData
{
    public bool HsPositive { get; set; }
    public float HsAmount { get; set; }

    public float SemaineIso { get; set; }
    public float Mois { get; set; }
    public float HeuresPlanifiees { get; set; }
    public float HeuresTravaillees { get; set; }
    public float NbJoursPlanifies { get; set; }
    public float NbJoursTravailles { get; set; }
    public float CongeJours { get; set; }

    public float HsMoyenne4Semaines { get; set; }
    public float HsMoyenne8Semaines { get; set; }
    public float Absences4Semaines { get; set; }
    public float Retards4Semaines { get; set; }
    public float HeuresTravaillees4Semaines { get; set; }

    public float CategorieId { get; set; }
    public string Departement { get; set; } = string.Empty;
    public float HeuresSemaineContrat { get; set; }
    public float MultiPointage { get; set; }
}

internal sealed class MlAbsencePrediction
{
    public bool Label { get; set; }
    public float Probability { get; set; }
    public float Score { get; set; }
}

internal sealed class MlHsAmountPrediction
{
    public float HsAmount { get; set; }
    public float Score { get; set; }
}

internal sealed class MlHsProbabilityPrediction
{
    public bool HsPositive { get; set; }
    public float Probability { get; set; }
    public float Score { get; set; }
}

/// <summary>
/// Mapping entre les échantillons métier et les classes ML.NET, plus le hash du contrat de
/// features. Une évolution de ce contrat invalide les modèles déjà stockés.
/// </summary>
internal static class MlNetData
{
    public static readonly string[] AbsenceFeatures =
    {
        nameof(MlAbsenceData.JourSemaine),
        nameof(MlAbsenceData.SemaineIso),
        nameof(MlAbsenceData.Mois),
        nameof(MlAbsenceData.JourDuMois),
        nameof(MlAbsenceData.VeilleFerie),
        nameof(MlAbsenceData.LendemainFerie),
        nameof(MlAbsenceData.DebutMois),
        nameof(MlAbsenceData.FinMois),
        nameof(MlAbsenceData.CategorieId),
        "DepartementEncoded",
        nameof(MlAbsenceData.HeuresSemaineContrat),
        nameof(MlAbsenceData.MultiPointage),
        nameof(MlAbsenceData.AncienneteJours),
        nameof(MlAbsenceData.Absences7j),
        nameof(MlAbsenceData.Absences30j),
        nameof(MlAbsenceData.Absences90j),
        nameof(MlAbsenceData.TauxAbsence30j),
        nameof(MlAbsenceData.Retards30j),
        nameof(MlAbsenceData.JoursTravailles7j),
        nameof(MlAbsenceData.JoursDepuisDerniereAbsence),
        nameof(MlAbsenceData.HeuresSupp4Semaines),
        nameof(MlAbsenceData.TauxAbsenceEquipe30j),
        nameof(MlAbsenceData.EffectifPrevuJour),
        nameof(MlAbsenceData.CongesPrevusJour),
        nameof(MlAbsenceData.HeuresPlanifiees),
        nameof(MlAbsenceData.PlanningConnu)
    };

    public static readonly string[] HsFeatures =
    {
        nameof(MlHsData.SemaineIso),
        nameof(MlHsData.Mois),
        nameof(MlHsData.HeuresPlanifiees),
        nameof(MlHsData.HeuresTravaillees),
        nameof(MlHsData.NbJoursPlanifies),
        nameof(MlHsData.NbJoursTravailles),
        nameof(MlHsData.CongeJours),
        nameof(MlHsData.HsMoyenne4Semaines),
        nameof(MlHsData.HsMoyenne8Semaines),
        nameof(MlHsData.Absences4Semaines),
        nameof(MlHsData.Retards4Semaines),
        nameof(MlHsData.HeuresTravaillees4Semaines),
        nameof(MlHsData.CategorieId),
        "DepartementEncoded",
        nameof(MlHsData.HeuresSemaineContrat),
        nameof(MlHsData.MultiPointage)
    };

    public static MlAbsenceData Map(MlAbsenceSample s) => new()
    {
        Label = s.Label,
        Weight = s.Weight,
        JourSemaine = s.JourSemaine,
        SemaineIso = s.SemaineIso,
        Mois = s.Mois,
        JourDuMois = s.JourDuMois,
        VeilleFerie = s.VeilleFerie ? 1f : 0f,
        LendemainFerie = s.LendemainFerie ? 1f : 0f,
        DebutMois = s.DebutMois ? 1f : 0f,
        FinMois = s.FinMois ? 1f : 0f,
        CategorieId = s.CategorieId,
        Departement = s.Departement ?? string.Empty,
        HeuresSemaineContrat = s.HeuresSemaineContrat,
        MultiPointage = s.MultiPointage ? 1f : 0f,
        AncienneteJours = s.AncienneteJours,
        Absences7j = s.Absences7j,
        Absences30j = s.Absences30j,
        Absences90j = s.Absences90j,
        TauxAbsence30j = s.TauxAbsence30j,
        Retards30j = s.Retards30j,
        JoursTravailles7j = s.JoursTravailles7j,
        JoursDepuisDerniereAbsence = s.JoursDepuisDerniereAbsence,
        HeuresSupp4Semaines = s.HeuresSupp4Semaines,
        TauxAbsenceEquipe30j = s.TauxAbsenceEquipe30j,
        EffectifPrevuJour = s.EffectifPrevuJour,
        CongesPrevusJour = s.CongesPrevusJour,
        HeuresPlanifiees = s.HeuresPlanifiees,
        PlanningConnu = s.PlanningConnu ? 1f : 0f
    };

    public static MlHsData Map(MlHsSample s) => new()
    {
        HsPositive = s.HsPositive,
        HsAmount = s.HsAmount,
        SemaineIso = s.SemaineIso,
        Mois = s.Mois,
        HeuresPlanifiees = s.HeuresPlanifiees,
        HeuresTravaillees = s.HeuresTravaillees,
        NbJoursPlanifies = s.NbJoursPlanifies,
        NbJoursTravailles = s.NbJoursTravailles,
        CongeJours = s.CongeJours,
        HsMoyenne4Semaines = s.HsMoyenne4Semaines,
        HsMoyenne8Semaines = s.HsMoyenne8Semaines,
        Absences4Semaines = s.Absences4Semaines,
        Retards4Semaines = s.Retards4Semaines,
        HeuresTravaillees4Semaines = s.HeuresTravaillees4Semaines,
        CategorieId = s.CategorieId,
        Departement = s.Departement ?? string.Empty,
        HeuresSemaineContrat = s.HeuresSemaineContrat,
        MultiPointage = s.MultiPointage ? 1f : 0f
    };

    public static string ComputeSchemaHash()
    {
        var contract = string.Join("|", AbsenceFeatures) + "||" + string.Join("|", HsFeatures);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(contract));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
