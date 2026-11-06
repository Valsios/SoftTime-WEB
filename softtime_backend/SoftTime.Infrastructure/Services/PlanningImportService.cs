using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

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
        var mode = ExcelCells.Fold(headers[0]);
        var shifts = await _uow.Repository<Planning>().ListAsync(_ => true, ct);
        var cards = (await _uow.Repository<CardPaie>().ListAsync(_ => true, ct))
            .Where(c => _planningDatabaseFilter.MatchesSage(c.SageBdd) && _planningDatabaseFilter.MatchesPointeuse(c.PointeuseBdd))
            .ToList();
        var categories = (await _uow.Repository<Categorie>().ListAsync(c => c.BddSage == _tenant.SageDb, ct))
            .Where(c => _planningDatabaseFilter.MatchesPointeuse(c.BddPointeuse))
            .ToList();
        var affect = (await _uow.Repository<CategorieSalarie>().ListAsync(a => a.BddSage == _tenant.SageDb, ct))
            .Where(a => _planningDatabaseFilter.MatchesPointeuse(a.BddPointeuse))
            .ToList();
        var repo = _uow.Repository<PlanningSalarie>();
        var imported = 0;
        var skipped = 0;
        var dateHeaders = headers.Skip(1).ToList();
        foreach (var row in rows)
        {
            var key = row.GetValueOrDefault(headers[0]) ?? string.Empty;
            IEnumerable<CardPaie> targets;
            if (mode.StartsWith("CATEG", StringComparison.Ordinal))
            {
                var cat = categories.FirstOrDefault(c => string.Equals(c.Intitule, key, StringComparison.OrdinalIgnoreCase));
                if (cat == null) { skipped++; continue; }
                var ids = affect.Where(a => a.IdCateg == cat.IdCategorie).Select(a => a.MatriculeSage).ToHashSet();
                targets = cards.Where(c => ids.Contains(c.Id));
            }
            else
            {
                var card = cards.FirstOrDefault(c => MemeIdentifiant(c.SageMatricule, key));
                if (card == null) { skipped++; continue; }
                targets = new[] { card };
            }

            foreach (var header in dateHeaders)
            {
                if (!ExcelCells.TryDate(header, out var date))
                    continue;

                var cellule = row.GetValueOrDefault(header)?.Trim();
                if (string.IsNullOrWhiteSpace(cellule))
                    continue;

                var off = string.Equals(cellule, "OFF", StringComparison.OrdinalIgnoreCase);
                int? noShift = null;
                DateTime? ha = null;
                DateTime? hd = null;
                var pause = TimeSpan.Zero;

                if (!off)
                {
                    if (!int.TryParse(cellule, out var numeroShift))
                    {
                        skipped++;
                        continue;
                    }
                    var shift = shifts.FirstOrDefault(s => s.NoShift == numeroShift);
                    if (shift == null || !PlanningGenerationRules.TryIntervalle(date, shift.Ha, shift.Hd, out ha, out hd))
                    {
                        skipped++;
                        continue;
                    }
                    noShift = numeroShift;
                    pause = shift.Pause ?? TimeSpan.Zero;
                }

                foreach (var card in targets)
                {
                    var existantes = await repo.ListAsync(
                        p => p.IdSal == card.Id
                             && p.DateP != null
                             && p.DateP.Value.Date == date.Date, ct);
                    var existing = existantes
                        .Where(p => _planningDatabaseFilter.MatchesSage(p.BddSage) && _planningDatabaseFilter.MatchesPointeuse(p.BddPointeuse))
                        .OrderBy(p => p.Id)
                        .FirstOrDefault();

                    if (existing == null)
                    {
                        await repo.AddAsync(new PlanningSalarie
                        {
                            IdSal = card.Id,
                            NoShift = noShift,
                            DateP = date.Date,
                            Ha = ha,
                            Hd = hd,
                            Pause = pause,
                            OffP = off,
                            BddSage = _tenant.SageDb,
                            BddPointeuse = _tenant.PointeuseDb
                        }, ct);
                    }
                    else
                    {
                        existing.NoShift = noShift;
                        existing.Ha = ha;
                        existing.Hd = hd;
                        existing.Pause = pause;
                        existing.OffP = off;
                        existing.BddSage = _tenant.SageDb;
                        existing.BddPointeuse = _tenant.PointeuseDb;
                        repo.Update(existing);
                    }
                    imported++;
                }
            }
        }
        await _uow.SaveChangesAsync(ct);
        return new ImportResultDto(imported, skipped,
            $"{imported} ligne(s) de planning importée(s), {skipped} élément(s) ignoré(s).");
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

        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;
        var cards = (await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == sageDb, ct))
            .Where(c => _planningDatabaseFilter.MatchesPointeuse(c.PointeuseBdd))
            .ToList();
        var categories = (await _uow.Repository<Categorie>().ListAsync(c => c.BddSage == sageDb, ct))
            .Where(c => _planningDatabaseFilter.MatchesPointeuse(c.BddPointeuse))
            .ToList();
        var affectations = (await _uow.Repository<CategorieSalarie>().ListAsync(a => a.BddSage == sageDb, ct))
            .Where(a => _planningDatabaseFilter.MatchesPointeuse(a.BddPointeuse))
            .ToList();
        var repo = _uow.Repository<PlanningSalarie>();
        var existantes = (await repo.ListAsync(p => p.BddSage == sageDb, ct))
            .Where(p => _planningDatabaseFilter.MatchesPointeuse(p.BddPointeuse))
            .ToList();

        var pas = parCategorie ? 2 : 3;   // (début, fin) ou (début, pause, fin)
        var ecrites = 0;
        var joursIgnores = 0;
        var lignesIgnores = 0;

        // La première ligne est un en-tête.
        foreach (var cells in lines.Skip(1))
        {
            if (cells.Length < 1 + pas)
            {
                lignesIgnores++;
                continue;
            }
            var cle = cells[0].Trim();
            if (!PlanningGenerationRules.TryLundi(PlanningGenerationRules.Cellule(cells, 1), out var lundi))
            {
                lignesIgnores++;
                continue;
            }

            List<CardPaie> cibles;
            TimeSpan? pauseCategorie = null;
            if (parCategorie)
            {
                var categorie = categories.FirstOrDefault(c =>
                    string.Equals(c.Intitule?.Trim(), cle, StringComparison.OrdinalIgnoreCase));
                if (categorie is null)
                {
                    lignesIgnores++;
                    continue;
                }
                pauseCategorie = categorie.Pause;
                var ids = affectations.Where(a => a.IdCateg == categorie.IdCategorie).Select(a => a.MatriculeSage).ToHashSet();
                cibles = cards.Where(c => ids.Contains(c.Id)).ToList();
            }
            else
            {
                var carte = cards.FirstOrDefault(c => MemeIdentifiant(c.SageMatricule, cle));
                cibles = carte is null ? new List<CardPaie>() : new List<CardPaie> { carte };
            }
            if (cibles.Count == 0)
            {
                lignesIgnores++;
                continue;
            }

            for (var jourSemaine = 0; jourSemaine < PlanningGenerationRules.JoursParSemaine; jourSemaine++)
            {
                var debutIndex = PlanningGenerationRules.IndiceDebut(jourSemaine, avecPauseParJour: !parCategorie);
                var debutTxt = PlanningGenerationRules.Cellule(cells, debutIndex);
                var finTxt = PlanningGenerationRules.Cellule(cells, debutIndex + pas - 1);
                var jour = lundi.AddDays(jourSemaine);
                var off = PlanningGenerationRules.EstOff(debutTxt);

                DateTime? ha = null;
                DateTime? hd = null;
                var pause = pauseCategorie;
                if (!off)
                {
                    if (!PlanningGenerationRules.TryIntervalle(jour, debutTxt, finTxt, out ha, out hd))
                    {
                        joursIgnores++;
                        continue;
                    }
                    if (!parCategorie && PlanningGenerationRules.TryHeure(PlanningGenerationRules.Cellule(cells, debutIndex + 1), out var pauseJour))
                        pause = pauseJour;
                }

                foreach (var carte in cibles)
                {
                    var entity = existantes
                        .Where(p => p.IdSal == carte.Id && p.DateP?.Date == jour)
                        .OrderBy(p => p.Id)
                        .FirstOrDefault();
                    if (entity is null)
                    {
                        entity = new PlanningSalarie
                        {
                            IdSal = carte.Id,
                            NoShift = null,
                            DateP = jour,
                            Ha = ha,
                            Hd = hd,
                            Pause = off ? TimeSpan.Zero : (pause ?? TimeSpan.Zero),
                            OffP = off,
                            BddSage = sageDb,
                            BddPointeuse = pointeuseDb
                        };
                        await repo.AddAsync(entity, ct);
                        existantes.Add(entity);
                    }
                    else
                    {
                        entity.NoShift = null;
                        entity.DateP = jour;
                        entity.Ha = ha;
                        entity.Hd = hd;
                        entity.Pause = off ? TimeSpan.Zero : (pause ?? TimeSpan.Zero);
                        entity.OffP = off;
                        entity.BddSage = sageDb;
                        entity.BddPointeuse = pointeuseDb;
                        repo.Update(entity);
                    }
                    ecrites++;
                }
            }
        }

        await _uow.SaveChangesAsync(ct);
        var message = $"{ecrites} jour(s) de planning généré(s)"
            + (joursIgnores > 0 ? $", {joursIgnores} jour(s) ignoré(s) (heures illisibles)" : string.Empty)
            + (lignesIgnores > 0 ? $", {lignesIgnores} ligne(s) ignorée(s)" : string.Empty) + ".";
        return new ImportResultDto(ecrites, joursIgnores + lignesIgnores, message);
    }

    /// <summary>Comparaison de matricule insensible au remplissage par des zéros (« 00369 » == « 369 »).</summary>
    private static bool MemeIdentifiant(string? a, string? b)
    {
        a = a?.Trim();
        b = b?.Trim();
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        if (long.TryParse(a, out var na) && long.TryParse(b, out var nb))
            return na == nb;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
