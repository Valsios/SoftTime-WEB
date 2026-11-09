using System.Globalization;

using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Dashboard;

internal static class DashboardIndicators
{

    internal static List<string> Valeurs(IEnumerable<string?> source) => source
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => v!.Trim())
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>
    /// Effectif, absences et retards d'un axe (catégorie, département).
    /// Seules les cartes actives sont comptées : c'est l'« effectif actif » annoncé par les KPI,
    /// et les événements sont donc restreints aux mêmes salariés pour que la ligne reste cohérente.
    /// </summary>
    internal static List<DashboardSliceDto> Repartition(
        DashboardData data, DashboardEvents events, Func<CardPaie, string?> axe) => data.Perimetre.Cartes
        .Where(c => c.Actif)
        .GroupBy(c => Normaliser(axe(c)), StringComparer.OrdinalIgnoreCase)
        .Select(g =>
        {
            var clesGroupe = g.Select(c => Cle(c.SageMatricule)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new DashboardSliceDto(
                g.Key,
                g.Count(),
                events.Absences.Count(a => clesGroupe.Contains(a.Cle)),
                events.Retards.Count(r => clesGroupe.Contains(r.Cle)),
                events.Retards.Where(r => clesGroupe.Contains(r.Cle)).Sum(r => r.Minutes),
                data.HsParCle.Where(kv => clesGroupe.Contains(kv.Key)).Sum(kv => kv.Value));
        })
        .OrderByDescending(s => s.Effectif)
        .ThenBy(s => s.Libelle, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>
    /// Classement d'assiduité : seuls les salariés ayant réellement pointé sont classés (sinon tout
    /// salarié sans pointage apparaîtrait « le plus ponctuel » à 0 jour travaillé).
    /// </summary>
    internal static DashboardAssiduiteDto Assiduite(DashboardData data, DashboardEvents events)
    {
        var lignes = LignesAssiduite(data, events);
        var plusPonctuels = lignes
            .OrderByDescending(l => l.TauxPonctualite)
            .ThenBy(l => l.JoursRetard)
            .ThenBy(l => l.MinutesRetard)
            .ThenBy(l => l.JoursAbsence)
            .ThenBy(l => l.Matricule, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        var moinsPonctuels = lignes
            .OrderBy(l => l.TauxPonctualite)
            .ThenByDescending(l => l.MinutesRetard)
            .ThenByDescending(l => l.JoursAbsence)
            .ThenBy(l => l.Matricule, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        return new DashboardAssiduiteDto(plusPonctuels, moinsPonctuels);
    }

    /// <summary>Une ligne par salarié ayant pointé au moins une fois sur la période.</summary>
    private static List<DashboardAssiduiteRowDto> LignesAssiduite(DashboardData data, DashboardEvents events)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var travail = events.Presences.GroupBy(p => p.Cle, comparer).ToDictionary(g => g.Key, g => g.Count(), comparer);
        var manques = events.Absences.GroupBy(a => a.Cle, comparer).ToDictionary(g => g.Key, g => g.Count(), comparer);
        var retardsParCle = events.Retards.GroupBy(r => r.Cle, comparer)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Minutes: g.Sum(r => r.Minutes)), comparer);

        return data.Cles
            .Where(kv => travail.ContainsKey(kv.Key))
            .Select(kv =>
            {
                var joursTravailles = travail[kv.Key];
                var retard = retardsParCle.GetValueOrDefault(kv.Key);
                return new DashboardAssiduiteRowDto(
                    kv.Key,
                    kv.Value.SageNom,
                    kv.Value.SagePrenom,
                    kv.Value.Departement,
                    data.CategorieParCle.GetValueOrDefault(kv.Key),
                    joursTravailles,
                    manques.GetValueOrDefault(kv.Key),
                    retard.Count,
                    retard.Minutes,
                    DashboardRules.TauxPonctualite(joursTravailles, retard.Count));
            })
            .ToList();
    }

    internal static string? LibelleCategorie(
        CardPaie carte,
        IReadOnlyDictionary<int, int> categorieParCarte,
        IReadOnlyDictionary<int, Categorie> categories) =>
        categorieParCarte.TryGetValue(carte.Id, out var idCat) && categories.TryGetValue(idCat, out var categorie)
            ? categorie.Intitule
            : null;

    internal static string Normaliser(string? valeur) =>
        string.IsNullOrWhiteSpace(valeur) ? "(non défini)" : valeur.Trim();

    internal static DashboardTopDto ToTop(
        string cle, int count, int minutes, IReadOnlyDictionary<string, CardPaie> cles)
    {
        cles.TryGetValue(cle, out var carte);
        return new DashboardTopDto(cle, carte?.SageNom, carte?.SagePrenom, carte?.Departement, count, minutes);
    }

    internal static bool EstNonTravaille(DateTime jour, IReadOnlySet<DateTime> feries) =>
        jour.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || feries.Contains(jour.Date);

    /// <summary>Clé de rapprochement insensible au remplissage par des zéros (« 00369 » == « 369 »).</summary>
    internal static string Cle(string? matricule)
    {
        var m = matricule?.Trim() ?? string.Empty;
        return long.TryParse(m, out var n) ? n.ToString(CultureInfo.InvariantCulture) : m.ToUpperInvariant();
    }
}
