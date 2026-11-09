using SoftTime.Application.Abstractions.EmployeePlanning;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.PlanningImport;

/// <summary>
/// Un import Excel de planning : première colonne = catégorie ou matricule, colonnes suivantes =
/// dates, cellules = numéro de shift ou « OFF ». Une instance par import.
/// </summary>
internal sealed class PlanningExcelImporter
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IPlanningDatabaseFilter _filter;
    private List<Planning> _shifts = new();
    private int _importees;
    private int _ignorees;

    public PlanningExcelImporter(IUnitOfWork uow, ITenantConnectionService tenant, IPlanningDatabaseFilter filter)
    {
        _uow = uow;
        _tenant = tenant;
        _filter = filter;
    }

    public async Task<ImportResultDto> ImportAsync(
        IReadOnlyList<Dictionary<string, string>> rows, IReadOnlyList<string> headers, CancellationToken ct)
    {
        var parCategorie = ExcelCells.Fold(headers[0]).StartsWith("CATEG", StringComparison.Ordinal);
        _shifts = await _uow.Repository<Planning>().ListAsync(_ => true, ct);
        var cards = (await _uow.Repository<CardPaie>().ListAsync(_ => true, ct))
            .Where(c => _filter.MatchesSage(c.SageBdd) && _filter.MatchesPointeuse(c.PointeuseBdd))
            .ToList();
        var cibles = await PlanningTargets.ChargerAsync(_uow, _tenant.SageDb, _filter, cards, ct);
        var dateHeaders = headers.Skip(1).ToList();

        foreach (var row in rows)
        {
            var targets = Cibles(cibles, parCategorie, row.GetValueOrDefault(headers[0]) ?? string.Empty);
            if (targets is null)
            {
                _ignorees++;
                continue;
            }
            foreach (var header in dateHeaders)
                await ImporterCelluleAsync(targets, header, row.GetValueOrDefault(header), ct);
        }

        await _uow.SaveChangesAsync(ct);
        return new ImportResultDto(_importees, _ignorees,
            $"{_importees} ligne(s) de planning importée(s), {_ignorees} élément(s) ignoré(s).");
    }

    /// <summary>Salariés visés par la ligne, ou <c>null</c> si la catégorie ou le matricule est inconnu.</summary>
    private static List<CardPaie>? Cibles(PlanningTargets cibles, bool parCategorie, string cle)
    {
        if (parCategorie)
        {
            var categorie = cibles.Categorie(cle, ignorerEspaces: false);
            return categorie is null ? null : cibles.Affectees(categorie);
        }
        var carte = cibles.Carte(cle);
        return carte is null ? null : new List<CardPaie> { carte };
    }

    private async Task ImporterCelluleAsync(
        List<CardPaie> targets, string header, string? valeur, CancellationToken ct)
    {
        if (!ExcelCells.TryDate(header, out var date))
            return;
        var cellule = valeur?.Trim();
        if (string.IsNullOrWhiteSpace(cellule))
            return;

        var jour = LireJour(date, cellule);
        if (jour is null)
        {
            _ignorees++;
            return;
        }
        foreach (var card in targets)
            await EnregistrerAsync(card, date.Date, jour.Value, ct);
    }

    /// <summary>« OFF » ou numéro d'un shift existant dont les heures sont lisibles.</summary>
    private PlanningDayValues? LireJour(DateTime date, string cellule)
    {
        if (string.Equals(cellule, "OFF", StringComparison.OrdinalIgnoreCase))
            return PlanningDayValues.Repos;
        if (!int.TryParse(cellule, out var numeroShift))
            return null;
        var shift = _shifts.FirstOrDefault(s => s.NoShift == numeroShift);
        if (shift == null || !PlanningGenerationRules.TryIntervalle(date, shift.Ha, shift.Hd, out var ha, out var hd))
            return null;
        return new PlanningDayValues(numeroShift, ha, hd, shift.Pause ?? TimeSpan.Zero, false);
    }

    private async Task EnregistrerAsync(CardPaie card, DateTime jour, PlanningDayValues valeurs, CancellationToken ct)
    {
        var repo = _uow.Repository<PlanningSalarie>();
        var existantes = await repo.ListAsync(
            p => p.IdSal == card.Id && p.DateP != null && p.DateP.Value.Date == jour, ct);
        var existing = existantes
            .Where(p => _filter.MatchesSage(p.BddSage) && _filter.MatchesPointeuse(p.BddPointeuse))
            .OrderBy(p => p.Id)
            .FirstOrDefault();

        if (existing == null)
        {
            var planning = new PlanningSalarie { IdSal = card.Id, DateP = jour };
            valeurs.Appliquer(planning, _tenant.SageDb, _tenant.PointeuseDb);
            await repo.AddAsync(planning, ct);
        }
        else
        {
            valeurs.Appliquer(existing, _tenant.SageDb, _tenant.PointeuseDb);
            repo.Update(existing);
        }
        _importees++;
    }
}
