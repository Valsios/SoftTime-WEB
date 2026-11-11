using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

public static class ClosingAnomalyFactory
{

    /// <summary>Origine des lignes régénérées par le pré-traitement (miroir de <c>PunchProcessingConfiguration.OrigineAuto</c>).</summary>
    internal const string OrigineAuto = "AUTO";

    /// <summary>Origine des corrections saisies à la main (miroir de <c>PunchProcessingConfiguration.OrigineManuel</c>).</summary>
    internal const string OrigineManuelle = "MANUEL";

    internal static ClosingAnomaly Anomalie(
        ClosingAnomalyType type, string cle, DateTime jour, string detail, decimal? heures = null,
        ClosingSeverity? gravite = null, bool? pointageExiste = null)
        => new(type, gravite ?? PayrollClosingRules.GraviteParDefaut(type), cle, jour.Date, detail, heures, pointageExiste);

    internal static string EffetDoublon(bool multiPointage) =>
        multiPointage ? "double passage sans effet sur la paie (multi-pointage)" : "peut fausser les heures";

    internal static bool AHeuresCorrigees(HeureCorrigee h) =>
        h.HEntree.HasValue || h.HSortie.HasValue || h.Hps.HasValue || h.Hpe.HasValue;

    /// <summary>Toutes les cartes d'une même clé matricule (règle C11 de l'état des absences).</summary>
    internal static Dictionary<int, string> CleParIdSal(IEnumerable<CardPaie> cartes)
    {
        var resultat = new Dictionary<int, string>();
        foreach (var carte in cartes)
        {
            var cle = MatriculeIdentity.Cle(carte.SageMatricule);
            if (cle.Length > 0)
                resultat[carte.Id] = cle;
        }
        return resultat;
    }

    /// <summary>Une carte par clé matricule : la plus petite ID, dans l'ordre des clés.</summary>
    internal static IEnumerable<(string Cle, CardPaie Carte)> CartesParCle(IEnumerable<CardPaie> cartes) =>
        cartes
            .Select(c => (Cle: MatriculeIdentity.Cle(c.SageMatricule), Carte: c))
            .Where(x => x.Cle.Length > 0)
            .GroupBy(x => x.Cle, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, g.OrderBy(x => x.Carte.Id).First().Carte));

    internal static string FormatHeure(TimeSpan heure) => heure.ToString(@"hh\:mm\:ss");
}
