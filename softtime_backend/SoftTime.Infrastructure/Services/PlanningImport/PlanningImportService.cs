using SoftTime.Application.Abstractions.EmployeePlanning;
using SoftTime.Application.Abstractions.PlanningImport;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.PlanningImport;

public sealed class PlanningImportService : IPlanningImportService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IPlanningDatabaseFilter _planningDatabaseFilter;

    public PlanningImportService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IPlanningDatabaseFilter planningDatabaseFilter)
    {
        _uow = uow;
        _tenant = tenant;
        _planningDatabaseFilter = planningDatabaseFilter;
    }

    public async Task<ImportResultDto> ImportExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, IReadOnlyList<string> headers, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        if (headers.Count == 0)
            return new ImportResultDto(0, 0, "Fichier vide.");
        return await new PlanningExcelImporter(_uow, _tenant, _planningDatabaseFilter).ImportAsync(rows, headers, ct);
    }

    /// <summary>
    /// Génère le planning d'une semaine depuis un CSV « catégorie ; lundi ; (début ; fin) × 7 ».
    /// Chaque ligne alimente tous les salariés affectés à la catégorie, avec la pause de la catégorie.
    /// Portage de <c>GenererPlanningParCategorieAsync</c> du desktop.
    /// Écart assumé : un jour « OFF » est enregistré <c>OffP = true</c> avec des heures nulles, au lieu
    /// de la date sentinelle 1800-01-01 du desktop (le calcul de semaine ignore déjà ces jours).
    /// </summary>
    public Task<ImportResultDto> GenerateFromCategoryCsvAsync(IReadOnlyList<string[]> lines, CancellationToken ct = default)
        => GenerateFromCsvAsync(lines, parCategorie: true, ct);

    /// <summary>
    /// Génère le planning d'une semaine depuis un CSV « matricule ; lundi ; (début ; pause ; fin) × 7 »,
    /// avec une pause saisie par jour. Portage de <c>GenererPlanningsAsync</c> du desktop.
    /// </summary>
    public Task<ImportResultDto> GenerateFromMatriculeCsvAsync(IReadOnlyList<string[]> lines, CancellationToken ct = default)
        => GenerateFromCsvAsync(lines, parCategorie: false, ct);

    private async Task<ImportResultDto> GenerateFromCsvAsync(
        IReadOnlyList<string[]> lines, bool parCategorie, CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        if (lines.Count <= 1)
            return new ImportResultDto(0, 0, "Fichier vide (en-tête seule).");
        return await new PlanningCsvGenerator(_uow, _tenant, _planningDatabaseFilter, parCategorie).GenerateAsync(lines, ct);
    }
}
