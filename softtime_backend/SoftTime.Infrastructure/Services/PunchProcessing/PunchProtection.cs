using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.PunchProcessing;

/// <summary>
/// Jours que le pré-traitement ne doit pas régénérer : semaines validées et jours portant une
/// correction conservée (manuelle, ou AUTO hors périmètre).
/// </summary>
internal sealed class PunchProtection
{
    private readonly Dictionary<string, HashSet<DateTime>> _semainesValidees;
    private readonly Dictionary<string, HashSet<DateTime>> _joursProteges;

    public PunchProtection(PunchProcessingData data)
    {
        _semainesValidees = data.SemainesValidees;
        CorrectionsAPurger = SelectionnerCorrectionsAPurger(data);
        _joursProteges = data.CorrectionsExistantes
            .Except(CorrectionsAPurger)
            .Where(h => h.DatePointageIn.HasValue && !string.IsNullOrWhiteSpace(h.MatriculeSage))
            .GroupBy(h => MatriculeIdentity.Cle(h.MatriculeSage))
            .ToDictionary(g => g.Key, g => g.Select(h => h.DatePointageIn!.Value.Date).ToHashSet());
    }

    /// <summary>
    /// Lignes AUTO à purger, limitées à ce qui va être régénéré : salariés du périmètre demandé
    /// hors semaines validées. Les autres lignes AUTO de la période ne sont pas retraitées.
    /// </summary>
    public IReadOnlyList<HeureCorrigee> CorrectionsAPurger { get; }

    public bool EstProtege(string cle, DateTime jour) =>
        (_joursProteges.TryGetValue(cle, out var jours) && jours.Contains(jour))
        || SemaineValidee(cle, jour);

    private bool SemaineValidee(string cle, DateTime jour)
    {
        var lundi = jour.AddDays(-(((int)jour.DayOfWeek + 6) % 7));
        return _semainesValidees.TryGetValue(cle, out var lundis) && lundis.Contains(lundi);
    }

    private List<HeureCorrigee> SelectionnerCorrectionsAPurger(PunchProcessingData data)
    {
        var clesPerimetre = data.Cards
            .Select(c => MatriculeIdentity.Cle(c.SageMatricule))
            .ToHashSet(StringComparer.Ordinal);
        return data.CorrectionsExistantes
            .Where(h => string.Equals(h.Origine, PunchProcessingConfiguration.OrigineAuto, StringComparison.OrdinalIgnoreCase))
            .Where(h => h.DatePointageIn.HasValue && !string.IsNullOrWhiteSpace(h.MatriculeSage))
            .Where(h => clesPerimetre.Contains(MatriculeIdentity.Cle(h.MatriculeSage)))
            .Where(h => !SemaineValidee(MatriculeIdentity.Cle(h.MatriculeSage), h.DatePointageIn!.Value.Date))
            .ToList();
    }
}
