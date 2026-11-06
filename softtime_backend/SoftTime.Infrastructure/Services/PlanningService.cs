using System.Linq.Expressions;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public class PlanningService : IPlanningService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IPlanningStatisticsReader _planningStats;

    public PlanningService(IUnitOfWork uow, ITenantConnectionService tenant, IPlanningStatisticsReader planningStats)
    {
        _uow = uow;
        _tenant = tenant;
        _planningStats = planningStats;
    }

    public async Task<IReadOnlyList<ShiftDto>> ListShiftsAsync(CancellationToken ct = default)
    {
        var list = await _uow.Repository<Planning>().ListReadOnlyAsync(_ => true, ct);
        return list.Select(ToShiftDto).ToList();
    }

    /// <summary>
    /// Liste paginée du catalogue des shifts. Le catalogue reste petit, mais l'endpoint suit le
    /// même contrat que les autres listes (page bornée, total serveur) pour éviter un découpage
    /// côté client.
    /// </summary>
    public async Task<PagedResult<ShiftDto>> ListShiftsPagedAsync(
        int? page, int? pageSize, string? search = null, CancellationToken ct = default)
        => await PagedLists.PageAsync(
            _uow.Repository<Planning>(),
            _ => true,
            q => q.OrderBy(s => s.NoShift)
                  .ThenBy(s => s.Intitule)
                  .ThenBy(s => s.IdPlanning),
            page,
            pageSize,
            ToShiftDto,
            search,
            new Expression<Func<Planning, string?>>[] { s => s.Intitule },
            ct);

    private static ShiftDto ToShiftDto(Planning s) => new(
        s.IdPlanning, s.NoShift, s.Ha, s.Pause, s.Hd, s.Intitule);

    public async Task<ShiftDto> SaveShiftAsync(ShiftDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<Planning>();
        Planning entity;
        if (dto.Id == 0)
        {
            entity = new Planning { NoShift = dto.NoShift, Ha = dto.Ha, Pause = dto.Pause, Hd = dto.Hd, Intitule = dto.Intitule };
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
            entity.NoShift = dto.NoShift;
            entity.Ha = dto.Ha;
            entity.Pause = dto.Pause;
            entity.Hd = dto.Hd;
            entity.Intitule = dto.Intitule;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return new ShiftDto(entity.IdPlanning, entity.NoShift, entity.Ha, entity.Pause, entity.Hd, entity.Intitule);
    }

    public async Task DeleteShiftAsync(decimal id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<Planning>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new NotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PlanningEmployeeDto>> ListEmployeesAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var cards = await _uow.Repository<CardPaie>().ListReadOnlyAsync(_ => true, ct);
        return cards
            .Where(c => MatchesSage(c.SageBdd))
            .OrderBy(c => c.SageMatricule)
            .ThenBy(c => c.SageNom)
            .Select(c => new PlanningEmployeeDto(c.Id, c.SageMatricule, c.SageNom, c.SagePrenom))
            .ToList();
    }

    public async Task<IReadOnlyList<EmployeePlanningDto>> ListEmployeePlanningAsync(DateTime from, DateTime to, int? cardId, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var fromDate = from.Date;
        var toDate = to.Date;
        var cards = (await _uow.Repository<CardPaie>().ListReadOnlyAsync(_ => true, ct))
            .Where(c => MatchesSage(c.SageBdd))
            .ToDictionary(c => c.Id);
        var list = await _uow.Repository<PlanningSalarie>().ListReadOnlyAsync(
            p => (!cardId.HasValue || p.IdSal == cardId)
                 && p.DateP != null
                 && p.DateP >= fromDate.AddDays(-1)
                 && p.DateP <= toDate.AddDays(2), ct);
        return list
            .Where(p => p.DateP.HasValue
                        && p.DateP.Value.Date >= fromDate
                        && p.DateP.Value.Date <= toDate
                        && MatchesSage(p.BddSage)
                        && (!cardId.HasValue || p.IdSal == cardId.Value))
            .Select(p =>
            {
                cards.TryGetValue(p.IdSal, out var card);
                return new EmployeePlanningDto(
                    p.Id, p.IdSal, p.NoShift, p.DateP, p.Ha, p.Pause, p.Hd, p.OffP,
                    card?.SageMatricule, card?.SageNom, card?.SagePrenom);
            })
            .OrderBy(p => p.Matricule)
            .ThenBy(p => p.DateP)
            .ToList();
    }

    /// <summary>
    /// Liste paginée des lignes de planning. Le filtre date/carte est traduit en SQL ; les cartes
    /// (petite table) sont chargées une fois pour exposer matricule/nom/prénom.
    /// </summary>
    public async Task<PagedResult<EmployeePlanningDto>> ListEmployeePlanningPagedAsync(
        DateTime from, DateTime to, int? cardId, int? page, int? pageSize,
        string? search = null, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var start = from.Date;
        var end = to.Date.AddDays(1);
        var cards = (await _uow.Repository<CardPaie>().ListReadOnlyAsync(EmployeeCardPredicate(sageDb), ct))
            .ToDictionary(c => c.Id);
        var hasSearch = SearchText.Terms(search).Count > 0;
        var cardIds = hasSearch
            ? cards.Values
                .Where(c => SearchText.Matches(search, c.SageMatricule, c.SageNom, c.SagePrenom))
                .Select(c => c.Id)
                .ToArray()
            : Array.Empty<int>();
        if (hasSearch && cardIds.Length == 0)
            return PagedLists.Page(Array.Empty<EmployeePlanningDto>(), page, pageSize);

        return await PagedLists.PageAsync(
            _uow.Repository<PlanningSalarie>(),
            p => p.DateP >= start
                 && p.DateP < end
                 && (p.BddSage == null || p.BddSage.Trim() == "" || p.BddSage.Trim() == sageDb)
                 && (!cardId.HasValue || p.IdSal == cardId.Value)
                 && (!hasSearch || cardIds.Contains(p.IdSal)),
            q => q.OrderBy(p => p.IdSal).ThenBy(p => p.DateP).ThenBy(p => p.Id),
            page,
            pageSize,
            p =>
            {
                cards.TryGetValue(p.IdSal, out var card);
                return new EmployeePlanningDto(
                    p.Id, p.IdSal, p.NoShift, p.DateP, p.Ha, p.Pause, p.Hd, p.OffP,
                    card?.SageMatricule, card?.SageNom, card?.SagePrenom);
            },
            ct);
    }

    /// <summary>Complet (export) : salariés + nombre de jours planifiés sur la période.</summary>
    public async Task<IReadOnlyList<PlanningEmployeeStatDto>> ListEmployeeStatsAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var counts = (await _planningStats.CountPlannedDaysAsync(sageDb, from, to, ct))
            .ToDictionary(x => x.CardPaieId, x => x.PlannedDays);
        var cards = await _uow.Repository<CardPaie>().ListReadOnlyAsync(EmployeeCardPredicate(sageDb), ct);
        return cards
            .Where(c => MatchesSage(c.SageBdd))
            .OrderBy(c => c.SageMatricule)
            .ThenBy(c => c.SageNom)
            .Select(c => new PlanningEmployeeStatDto(
                c.Id, c.SageMatricule, c.SageNom, c.SagePrenom, counts.GetValueOrDefault(c.Id)))
            .ToList();
    }

    /// <summary>Grille paginée des salariés avec leur nombre de jours planifiés.</summary>
    public async Task<PagedResult<PlanningEmployeeStatDto>> ListEmployeeStatsPagedAsync(
        DateTime from, DateTime to, int? page, int? pageSize,
        string? search = null, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var counts = (await _planningStats.CountPlannedDaysAsync(sageDb, from, to, ct))
            .ToDictionary(x => x.CardPaieId, x => x.PlannedDays);

        return await PagedLists.PageAsync(
            _uow.Repository<CardPaie>(),
            EmployeeCardPredicate(sageDb),
            q => q.OrderBy(c => c.SageMatricule).ThenBy(c => c.SageNom).ThenBy(c => c.Id),
            page,
            pageSize,
            c => new PlanningEmployeeStatDto(
                c.Id, c.SageMatricule, c.SageNom, c.SagePrenom, counts.GetValueOrDefault(c.Id)),
            search,
            new Expression<Func<CardPaie, string?>>[]
            {
                c => c.SageMatricule,
                c => c.SageNom,
                c => c.SagePrenom,
            },
            ct);
    }

    /// <summary>Filtre tenant des cartes : <c>SAGE_BDD</c> null/vide est accepté (lignes héritées).</summary>
    private static Expression<Func<CardPaie, bool>> EmployeeCardPredicate(string sageDb)
        => c => c.SageBdd == null || c.SageBdd.Trim() == "" || c.SageBdd.Trim() == sageDb;

    public async Task SaveEmployeePlanningAsync(IReadOnlyCollection<EmployeePlanningDto> rows, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<PlanningSalarie>();
        var shifts = await _uow.Repository<Planning>().ListAsync(_ => true, ct);
        var cards = (await _uow.Repository<CardPaie>().ListAsync(_ => true, ct))
            .Where(c => MatchesSage(c.SageBdd) && MatchesPointeuse(c.PointeuseBdd))
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var dto in rows)
        {
            if (!dto.DateP.HasValue)
                throw new DomainException("La date du planning est requise.");
            if (!cards.ContainsKey(dto.CardPaieId))
                throw new DomainException($"Salarié introuvable pour la carte {dto.CardPaieId}.");

            var date = dto.DateP.Value.Date;
            var shift = dto.NoShift is int noShift
                ? shifts.FirstOrDefault(s => s.NoShift == noShift)
                : null;
            if (dto.NoShift is int shiftDemande && shift is null)
                throw new DomainException($"Shift {shiftDemande} introuvable.");

            PlanningSalarie? entity = null;
            if (dto.Id != 0)
            {
                entity = await repo.GetByIdAsync(dto.Id, ct)
                    ?? throw new DomainException($"Planning {dto.Id} introuvable.");
                if (!MatchesSage(entity.BddSage) || !MatchesPointeuse(entity.BddPointeuse))
                    throw new DomainException("Planning hors du périmètre de la base active.");
            }

            if (entity is null)
            {
                var existantes = await repo.ListAsync(
                    p => p.IdSal == dto.CardPaieId
                         && p.DateP != null
                         && p.DateP.Value.Date == date, ct);
                entity = existantes
                    .Where(p => MatchesSage(p.BddSage) && MatchesPointeuse(p.BddPointeuse))
                    .OrderBy(p => p.Id)
                    .FirstOrDefault();
            }

            var off = dto.Off == true;
            DateTime? ha = null;
            DateTime? hd = null;
            var pause = TimeSpan.Zero;

            if (!off)
            {
                // Une ligne héritée peut ne pas avoir de NoSHIFT : on conserve alors ses heures
                // existantes si l'écran n'en fournit pas.
                var debut = dto.Ha?.TimeOfDay ?? shift?.Ha ?? entity?.Ha?.TimeOfDay;
                var fin = dto.Hd?.TimeOfDay ?? shift?.Hd ?? entity?.Hd?.TimeOfDay;
                if (!PlanningGenerationRules.TryIntervalle(date, debut, fin, out ha, out hd))
                    throw new DomainException(
                        $"Heures d'entrée/sortie manquantes ou invalides pour le planning du {date:dd/MM/yyyy}.");
                pause = dto.Pause ?? shift?.Pause ?? entity?.Pause ?? TimeSpan.Zero;
            }

            if (entity == null)
            {
                await repo.AddAsync(new PlanningSalarie
                {
                    IdSal = dto.CardPaieId,
                    NoShift = dto.NoShift,
                    DateP = date,
                    Ha = ha,
                    Pause = pause,
                    Hd = hd,
                    OffP = off,
                    BddSage = _tenant.SageDb,
                    BddPointeuse = _tenant.PointeuseDb
                }, ct);
            }
            else
            {
                entity.IdSal = dto.CardPaieId;
                entity.NoShift = dto.NoShift;
                entity.DateP = date;
                entity.Ha = ha;
                entity.Pause = pause;
                entity.Hd = hd;
                entity.OffP = off;
                entity.BddSage = _tenant.SageDb;
                entity.BddPointeuse = _tenant.PointeuseDb;
                repo.Update(entity);
            }
        }
        await _uow.SaveChangesAsync(ct);
    }

    public async Task DeleteEmployeePlanningAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<PlanningSalarie>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new NotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ImportResultDto> ImportExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, IReadOnlyList<string> headers, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        if (headers.Count == 0)
            return new ImportResultDto(0, 0, "Fichier vide.");
        var mode = ExcelCells.Fold(headers[0]);
        var shifts = await _uow.Repository<Planning>().ListAsync(_ => true, ct);
        var cards = (await _uow.Repository<CardPaie>().ListAsync(_ => true, ct))
            .Where(c => MatchesSage(c.SageBdd) && MatchesPointeuse(c.PointeuseBdd))
            .ToList();
        var categories = (await _uow.Repository<Categorie>().ListAsync(c => c.BddSage == _tenant.SageDb, ct))
            .Where(c => MatchesPointeuse(c.BddPointeuse))
            .ToList();
        var affect = (await _uow.Repository<CategorieSalarie>().ListAsync(a => a.BddSage == _tenant.SageDb, ct))
            .Where(a => MatchesPointeuse(a.BddPointeuse))
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
                        .Where(p => MatchesSage(p.BddSage) && MatchesPointeuse(p.BddPointeuse))
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
            .Where(c => MatchesPointeuse(c.PointeuseBdd))
            .ToList();
        var categories = (await _uow.Repository<Categorie>().ListAsync(c => c.BddSage == sageDb, ct))
            .Where(c => MatchesPointeuse(c.BddPointeuse))
            .ToList();
        var affectations = (await _uow.Repository<CategorieSalarie>().ListAsync(a => a.BddSage == sageDb, ct))
            .Where(a => MatchesPointeuse(a.BddPointeuse))
            .ToList();
        var repo = _uow.Repository<PlanningSalarie>();
        var existantes = (await repo.ListAsync(p => p.BddSage == sageDb, ct))
            .Where(p => MatchesPointeuse(p.BddPointeuse))
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

    public async Task<ImportResultDto> ImportShiftsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        var repo = _uow.Repository<Planning>();
        var existing = await repo.ListAsync(_ => true, ct);
        var imported = 0;
        var skipped = 0;
        foreach (var row in rows)
        {
            var noRaw = ExcelCells.Get(row, "NoSHIFT", "N° SHIFT", "No SHIFT", "SHIFT", "N°");
            if (!int.TryParse(noRaw, out var noShift))
            {
                skipped++;
                continue;
            }
            ExcelCells.TryTime(ExcelCells.Get(row, "ENTREE", "HA", "Heure arrivée", "Arrivee"), out var ha);
            ExcelCells.TryTime(ExcelCells.Get(row, "PAUSE", "Pause"), out var pause);
            ExcelCells.TryTime(ExcelCells.Get(row, "SORTIE", "HD", "Heure départ", "Depart"), out var hd);
            var intitule = ExcelCells.Get(row, "INTITULE", "Intitulé", "Libelle");
            var entity = existing.FirstOrDefault(s => s.NoShift == noShift);
            if (entity == null)
            {
                entity = new Planning { NoShift = noShift, Ha = ha == default ? null : ha, Pause = pause == default ? null : pause, Hd = hd == default ? null : hd, Intitule = intitule };
                await repo.AddAsync(entity, ct);
                existing.Add(entity);
            }
            else
            {
                entity.Ha = ha == default ? entity.Ha : ha;
                entity.Pause = pause == default ? entity.Pause : pause;
                entity.Hd = hd == default ? entity.Hd : hd;
                if (!string.IsNullOrWhiteSpace(intitule))
                    entity.Intitule = intitule;
                repo.Update(entity);
            }
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        return new ImportResultDto(imported, skipped,
            $"{imported} shift(s) importé(s), {skipped} ligne(s) ignorée(s).");
    }

    private bool MatchesSage(string? stored)
    {
        var sage = _tenant.SageDb;
        if (string.IsNullOrWhiteSpace(stored))
            return true;
        return string.Equals(stored.Trim(), sage.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesPointeuse(string? stored)
    {
        var pointeuse = _tenant.PointeuseDb;
        if (string.IsNullOrWhiteSpace(stored))
            return true;
        return string.Equals(stored.Trim(), pointeuse.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
