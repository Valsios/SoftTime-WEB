using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IPlanningImportService
{
    Task<ImportResultDto> ImportExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, IReadOnlyList<string> headers, CancellationToken ct = default);

    /// <summary>
    /// Génère le planning d'une semaine depuis un CSV « catégorie ; lundi ; (début ; fin) × 7 ».
    /// Chaque ligne alimente tous les salariés affectés à la catégorie, avec la pause de la catégorie.
    /// Portage de <c>GenererPlanningParCategorieAsync</c> du desktop.
    /// Écart assumé : un jour « OFF » est enregistré <c>OffP = true</c> avec des heures nulles, au lieu
    /// de la date sentinelle 1800-01-01 du desktop (le calcul de semaine ignore déjà ces jours).
    /// </summary>
    Task<ImportResultDto> GenerateFromCategoryCsvAsync(IReadOnlyList<string[]> lines, CancellationToken ct = default);

    /// <summary>
    /// Génère le planning d'une semaine depuis un CSV « matricule ; lundi ; (début ; pause ; fin) × 7 »,
    /// avec une pause saisie par jour. Portage de <c>GenererPlanningsAsync</c> du desktop.
    /// </summary>
    Task<ImportResultDto> GenerateFromMatriculeCsvAsync(IReadOnlyList<string[]> lines, CancellationToken ct = default);
}
