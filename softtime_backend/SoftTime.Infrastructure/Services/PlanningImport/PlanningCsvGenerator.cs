using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.PlanningImport;

/// <summary>
/// Une génération de planning hebdomadaire depuis un CSV : « catégorie ; lundi ; (début ; fin) × 7 »
/// ou « matricule ; lundi ; (début ; pause ; fin) × 7 ». Une instance par génération.
/// </summary>
internal sealed class PlanningCsvGenerator
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IPlanningDatabaseFilter _filter;
    private readonly bool _parCategorie;

    /// <summary>Cellules par jour : (début, fin) ou (début, pause, fin).</summary>
    private readonly int _pas;
    private PlanningTargets? _cibles;
    private List<PlanningSalarie> _existantes = new();
    private int _ecrites;
    private int _joursIgnores;
    private int _lignesIgnorees;

    public PlanningCsvGenerator(
        IUnitOfWork uow, ITenantConnectionService tenant, IPlanningDatabaseFilter filter, bool parCategorie)
    {
        _uow = uow;
        _tenant = tenant;
        _filter = filter;
        _parCategorie = parCategorie;
        _pas = parCategorie ? 2 : 3;
    }

    public async Task<ImportResultDto> GenerateAsync(IReadOnlyList<string[]> lines, CancellationToken ct)
    {
        await ChargerAsync(ct);
        // La première ligne est un en-tête.
        foreach (var cells in lines.Skip(1))
            await GenererLigneAsync(cells, ct);

        await _uow.SaveChangesAsync(ct);
        var message = $"{_ecrites} jour(s) de planning généré(s)"
            + (_joursIgnores > 0 ? $", {_joursIgnores} jour(s) ignoré(s) (heures illisibles)" : string.Empty)
            + (_lignesIgnorees > 0 ? $", {_lignesIgnorees} ligne(s) ignorée(s)" : string.Empty) + ".";
        return new ImportResultDto(_ecrites, _joursIgnores + _lignesIgnorees, message);
    }

    private async Task ChargerAsync(CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var cards = (await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == sageDb, ct))
            .Where(c => _filter.MatchesPointeuse(c.PointeuseBdd))
            .ToList();
        _cibles = await PlanningTargets.ChargerAsync(_uow, sageDb, _filter, cards, ct);
        _existantes = (await _uow.Repository<PlanningSalarie>().ListAsync(p => p.BddSage == sageDb, ct))
            .Where(p => _filter.MatchesPointeuse(p.BddPointeuse))
            .ToList();
    }

    private async Task GenererLigneAsync(string[] cells, CancellationToken ct)
    {
        if (cells.Length < 1 + _pas
            || !PlanningGenerationRules.TryLundi(PlanningGenerationRules.Cellule(cells, 1), out var lundi))
        {
            _lignesIgnorees++;
            return;
        }
        var (cibles, pauseCategorie) = Cibles(cells[0].Trim());
        if (cibles.Count == 0)
        {
            _lignesIgnorees++;
            return;
        }

        for (var jourSemaine = 0; jourSemaine < PlanningGenerationRules.JoursParSemaine; jourSemaine++)
        {
            var jour = lundi.AddDays(jourSemaine);
            var valeurs = LireJour(cells, jour, jourSemaine, pauseCategorie);
            if (valeurs is null)
            {
                _joursIgnores++;
                continue;
            }
            foreach (var carte in cibles)
                await EnregistrerAsync(carte, jour, valeurs.Value, ct);
        }
    }

    /// <summary>Salariés visés et pause de la catégorie (mode catégorie uniquement).</summary>
    private (List<CardPaie> Cibles, TimeSpan? PauseCategorie) Cibles(string cle)
    {
        if (_parCategorie)
        {
            var categorie = _cibles!.Categorie(cle, ignorerEspaces: true);
            return categorie is null ? (new List<CardPaie>(), null) : (_cibles.Affectees(categorie), categorie.Pause);
        }
        var carte = _cibles!.Carte(cle);
        return (carte is null ? new List<CardPaie>() : new List<CardPaie> { carte }, null);
    }

    /// <summary>Journée lue dans le CSV, ou <c>null</c> si les heures sont illisibles.</summary>
    private PlanningDayValues? LireJour(string[] cells, DateTime jour, int jourSemaine, TimeSpan? pauseCategorie)
    {
        var debutIndex = PlanningGenerationRules.IndiceDebut(jourSemaine, avecPauseParJour: !_parCategorie);
        var debutTxt = PlanningGenerationRules.Cellule(cells, debutIndex);
        var finTxt = PlanningGenerationRules.Cellule(cells, debutIndex + _pas - 1);
        if (PlanningGenerationRules.EstOff(debutTxt))
            return PlanningDayValues.Repos;
        if (!PlanningGenerationRules.TryIntervalle(jour, debutTxt, finTxt, out var ha, out var hd))
            return null;

        var pause = pauseCategorie;
        if (!_parCategorie && PlanningGenerationRules.TryHeure(PlanningGenerationRules.Cellule(cells, debutIndex + 1), out var pauseJour))
            pause = pauseJour;
        return new PlanningDayValues(null, ha, hd, pause ?? TimeSpan.Zero, false);
    }

    private async Task EnregistrerAsync(CardPaie carte, DateTime jour, PlanningDayValues valeurs, CancellationToken ct)
    {
        var repo = _uow.Repository<PlanningSalarie>();
        var entity = _existantes
            .Where(p => p.IdSal == carte.Id && p.DateP?.Date == jour)
            .OrderBy(p => p.Id)
            .FirstOrDefault();
        if (entity is null)
        {
            entity = new PlanningSalarie { IdSal = carte.Id, DateP = jour };
            valeurs.Appliquer(entity, _tenant.SageDb, _tenant.PointeuseDb);
            await repo.AddAsync(entity, ct);
            _existantes.Add(entity);
        }
        else
        {
            entity.DateP = jour;
            valeurs.Appliquer(entity, _tenant.SageDb, _tenant.PointeuseDb);
            repo.Update(entity);
        }
        _ecrites++;
    }
}
