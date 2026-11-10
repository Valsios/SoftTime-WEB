using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Règle pure du statut de clôture :
/// <list type="bullet">
///   <item>Prêt : aucune anomalie critique ;</item>
///   <item>Bloquant : critiques ÷ jours-employé contrôlés ≥ <c>ratioBloquant</c> ;</item>
///   <item>À corriger : sinon.</item>
/// </list>
/// Sans jour-employé contrôlé, aucun statut (comme le score). Le seuil par défaut
/// (<see cref="DefaultCriticalRatioBlocking"/>) est PROVISOIRE, à confirmer par le manager.
/// </summary>
public static class ClosingStatusRules
{
    public const decimal DefaultCriticalRatioBlocking = 0.05m;

    public static ClosingStatus? Statut(int critiques, int joursEmployeControles, decimal ratioBloquant)
    {
        if (joursEmployeControles <= 0)
            return null;
        if (critiques <= 0)
            return ClosingStatus.Pret;
        return (decimal)critiques / joursEmployeControles >= ratioBloquant
            ? ClosingStatus.Bloquant
            : ClosingStatus.ACorriger;
    }

    public static ClosingStatus? Statut(IEnumerable<ClosingAnomaly> anomalies, int joursEmployeControles, decimal ratioBloquant)
        => Statut(anomalies.Count(a => a.Severity == ClosingSeverity.Critique), joursEmployeControles, ratioBloquant);

    /// <summary>Libellé affichable du statut.</summary>
    public static string Libelle(ClosingStatus statut) => statut switch
    {
        ClosingStatus.Pret => "Prêt",
        ClosingStatus.ACorriger => "À corriger",
        ClosingStatus.Bloquant => "Bloquant",
        _ => statut.ToString()
    };
}
