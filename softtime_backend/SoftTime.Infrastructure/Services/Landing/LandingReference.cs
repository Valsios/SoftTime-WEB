using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Landing;

/// <summary>Paramétrage de la société dont dépend le calcul HS d'un salarié.</summary>
internal sealed class LandingReference
{
    public bool MultiPointage { get; init; }
    public IReadOnlyList<Categorie> Categories { get; init; } = Array.Empty<Categorie>();
    public IReadOnlyList<CategorieSalarie> Affectations { get; init; } = Array.Empty<CategorieSalarie>();
    public IReadOnlyList<ToleranceRetard> Tolerances { get; init; } = Array.Empty<ToleranceRetard>();
    public IReadOnlyList<Majoration> Majorations { get; init; } = Array.Empty<Majoration>();
    public IReadOnlySet<DateTime> Feries { get; init; } = new HashSet<DateTime>();
    public IReadOnlyDictionary<string, byte?> Civilites { get; init; } = new Dictionary<string, byte?>();

    /// <summary>Heures contractuelles, pause et tolérances de la catégorie du salarié ; 40 h sans catégorie.</summary>
    public OvertimeWeekConfig Config(CardPaie card)
    {
        var affectation = Affectations.FirstOrDefault(a => a.MatriculeSage == card.Id);
        var categorie = affectation == null
            ? null
            : Categories.FirstOrDefault(c => c.IdCategorie == affectation.IdCateg);
        var tolerance = categorie == null
            ? null
            : Tolerances.FirstOrDefault(t => t.IdCategorie == categorie.IdCategorie && !t.TypesTol);

        return new OvertimeWeekConfig(
            MultiPointage: MultiPointage,
            HeuresSemaine: categorie?.HeureSemaine ?? OvertimeRules.DefaultWeeklyHours,
            PauseCategorie: categorie?.Pause,
            ToleranceEntreeMinutes: tolerance?.Tolerance ?? 0,
            PasSortieMinutes: tolerance?.Sortie,
            CotationNuit: Cotation("NUIT"),
            CotationDimanche: Cotation("DIMANCHE"),
            CotationFerie: Cotation("FERIE"),
            EstFemme: OvertimeDayRules.EstFemme(Civilites.GetValueOrDefault(MatriculeIdentity.Cle(card.SageMatricule))));
    }

    private decimal Cotation(string type)
        => Majorations
            .Where(m => string.Equals(m.Mojoration?.Trim(), type, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Cotation ?? 0m)
            .DefaultIfEmpty(0m)
            .Max();
}
