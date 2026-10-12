using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

public class PlanningService
{
    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;

    public PlanningService(IUnitOfWork uow, TenantConnectionService tenant)
    {
        _uow = uow;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ShiftDto>> ListShiftsAsync(CancellationToken ct = default)
    {
        var list = await _uow.Repository<T_PLANNING>().ListAsync(_ => true, ct);
        return list.Select(s => new ShiftDto(s.IDPLANNING, s.NoSHIFT, s.HA, s.PAUSE, s.HD, s.Intitule)).ToList();
    }

    public async Task<ShiftDto> SaveShiftAsync(ShiftDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_PLANNING>();
        T_PLANNING entity;
        if (dto.Id == 0)
        {
            entity = new T_PLANNING { NoSHIFT = dto.NoShift, HA = dto.Ha, PAUSE = dto.Pause, HD = dto.Hd, Intitule = dto.Intitule };
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.NoSHIFT = dto.NoShift;
            entity.HA = dto.Ha;
            entity.PAUSE = dto.Pause;
            entity.HD = dto.Hd;
            entity.Intitule = dto.Intitule;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return new ShiftDto(entity.IDPLANNING, entity.NoSHIFT, entity.HA, entity.PAUSE, entity.HD, entity.Intitule);
    }

    public async Task DeleteShiftAsync(decimal id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_PLANNING>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PlanningEmployeeDto>> ListEmployeesAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var cards = await _uow.Repository<T_CARDPAIE>().ListAsync(_ => true, ct);
        return cards
            .Where(c => MatchesSage(c.SAGE_BDD))
            .OrderBy(c => c.SAGE_MATRICULE)
            .ThenBy(c => c.SAGE_NOM)
            .Select(c => new PlanningEmployeeDto(c.ID, c.SAGE_MATRICULE, c.SAGE_NOM, c.SAGE_PRENOM))
            .ToList();
    }

    public async Task<IReadOnlyList<EmployeePlanningDto>> ListEmployeePlanningAsync(DateTime from, DateTime to, int? cardId, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var fromDate = from.Date;
        var toDate = to.Date;
        var cards = (await _uow.Repository<T_CARDPAIE>().ListAsync(_ => true, ct))
            .Where(c => MatchesSage(c.SAGE_BDD))
            .ToDictionary(c => c.ID);
        var list = await _uow.Repository<T_PLANNING_SAL>().ListAsync(
            p => (!cardId.HasValue || p.IDSAL == cardId)
                 && p.DateP != null
                 && p.DateP >= fromDate.AddDays(-1)
                 && p.DateP <= toDate.AddDays(2), ct);
        return list
            .Where(p => p.DateP.HasValue
                        && p.DateP.Value.Date >= fromDate
                        && p.DateP.Value.Date <= toDate
                        && MatchesSage(p.BDD_SAGE)
                        && (!cardId.HasValue || p.IDSAL == cardId.Value))
            .Select(p =>
            {
                cards.TryGetValue(p.IDSAL, out var card);
                return new EmployeePlanningDto(
                    p.ID, p.IDSAL, p.NoSHIFT, p.DateP, p.HA, p.Pause, p.HD, p.OffP,
                    card?.SAGE_MATRICULE, card?.SAGE_NOM, card?.SAGE_PRENOM);
            })
            .OrderBy(p => p.Matricule)
            .ThenBy(p => p.DateP)
            .ToList();
    }

    public async Task SaveEmployeePlanningAsync(IReadOnlyCollection<EmployeePlanningDto> rows, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<T_PLANNING_SAL>();
        var shifts = await _uow.Repository<T_PLANNING>().ListAsync(_ => true, ct);
        foreach (var dto in rows)
        {
            var date = (dto.DateP ?? DateTime.Today).Date;
            var shift = dto.NoShift is int n ? shifts.FirstOrDefault(s => s.NoSHIFT == n) : null;
            var ha = dto.Ha ?? Combine(date, shift?.HA);
            var hd = dto.Hd ?? Combine(date, shift?.HD);
            var pause = dto.Pause ?? shift?.PAUSE;
            T_PLANNING_SAL? entity = null;
            if (dto.Id != 0)
                entity = await repo.GetByIdAsync(dto.Id, ct);
            entity ??= (await repo.ListAsync(
                p => p.IDSAL == dto.CardPaieId
                     && p.DateP != null
                     && p.DateP.Value.Date == date
                     && p.BDD_SAGE == _tenant.SageDb, ct)).FirstOrDefault();
            if (entity == null)
            {
                await repo.AddAsync(new T_PLANNING_SAL
                {
                    IDSAL = dto.CardPaieId,
                    NoSHIFT = dto.NoShift,
                    DateP = date,
                    HA = ha,
                    Pause = pause,
                    HD = hd,
                    OffP = dto.Off,
                    BDD_SAGE = _tenant.SageDb,
                    BDD_POINTEUSE = _tenant.PointeuseDb
                }, ct);
            }
            else
            {
                entity.IDSAL = dto.CardPaieId;
                entity.NoSHIFT = dto.NoShift;
                entity.DateP = date;
                entity.HA = ha;
                entity.Pause = pause;
                entity.HD = hd;
                entity.OffP = dto.Off;
                repo.Update(entity);
            }
        }
        await _uow.SaveChangesAsync(ct);
    }

    public async Task DeleteEmployeePlanningAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_PLANNING_SAL>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ImportResultDto> ImportExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, IReadOnlyList<string> headers, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        if (headers.Count == 0)
            return new ImportResultDto(0, 0, "Fichier vide.");
        var mode = ExcelCells.Fold(headers[0]);
        var shifts = await _uow.Repository<T_PLANNING>().ListAsync(_ => true, ct);
        var cards = await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == _tenant.SageDb, ct);
        var categories = await _uow.Repository<T_CATEGORIE>().ListAsync(c => c.BDD_SAGE == _tenant.SageDb, ct);
        var affect = await _uow.Repository<T_CAT_SAL>().ListAsync(a => a.BDD_SAGE == _tenant.SageDb, ct);
        var repo = _uow.Repository<T_PLANNING_SAL>();
        var imported = 0;
        var skipped = 0;
        var dateHeaders = headers.Skip(1).ToList();
        foreach (var row in rows)
        {
            var key = row.GetValueOrDefault(headers[0]) ?? string.Empty;
            IEnumerable<T_CARDPAIE> targets;
            if (mode.StartsWith("CATEG", StringComparison.Ordinal))
            {
                var cat = categories.FirstOrDefault(c => string.Equals(c.INTITULE, key, StringComparison.OrdinalIgnoreCase));
                if (cat == null) { skipped++; continue; }
                var ids = affect.Where(a => a.ID_CATEG == cat.IDCATEGORIE).Select(a => a.MATRICULE_SAGE).ToHashSet();
                targets = cards.Where(c => ids.Contains(c.ID));
            }
            else
            {
                var card = cards.FirstOrDefault(c => c.SAGE_MATRICULE == key);
                if (card == null) { skipped++; continue; }
                targets = new[] { card };
            }
            foreach (var header in dateHeaders)
            {
                if (!ExcelCells.TryDate(header, out var date))
                    continue;
                if (!int.TryParse(row.GetValueOrDefault(header), out var noShift))
                    continue;
                var shift = shifts.FirstOrDefault(s => s.NoSHIFT == noShift);
                if (shift == null) { skipped++; continue; }
                foreach (var card in targets)
                {
                    var existing = (await repo.ListAsync(
                        p => p.IDSAL == card.ID
                             && p.DateP != null
                             && p.DateP.Value.Date == date.Date
                             && p.BDD_SAGE == _tenant.SageDb, ct)).FirstOrDefault();
                    var ha = Combine(date, shift.HA);
                    var hd = Combine(date, shift.HD);
                    if (existing == null)
                    {
                        await repo.AddAsync(new T_PLANNING_SAL
                        {
                            IDSAL = card.ID,
                            NoSHIFT = noShift,
                            DateP = date.Date,
                            HA = ha,
                            HD = hd,
                            Pause = shift.PAUSE,
                            OffP = false,
                            BDD_SAGE = _tenant.SageDb,
                            BDD_POINTEUSE = _tenant.PointeuseDb
                        }, ct);
                    }
                    else
                    {
                        existing.NoSHIFT = noShift;
                        existing.HA = ha;
                        existing.HD = hd;
                        existing.Pause = shift.PAUSE;
                        existing.OffP = false;
                        repo.Update(existing);
                    }
                    imported++;
                }
            }
        }
        await _uow.SaveChangesAsync(ct);
        return new ImportResultDto(imported, skipped, "Planning importé.");
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
        var cards = await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == sageDb, ct);
        var categories = await _uow.Repository<T_CATEGORIE>().ListAsync(c => c.BDD_SAGE == sageDb, ct);
        var affectations = await _uow.Repository<T_CAT_SAL>().ListAsync(a => a.BDD_SAGE == sageDb, ct);
        var repo = _uow.Repository<T_PLANNING_SAL>();
        var existantes = await repo.ListAsync(p => p.BDD_SAGE == sageDb, ct);

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

            List<T_CARDPAIE> cibles;
            TimeSpan? pauseCategorie = null;
            if (parCategorie)
            {
                var categorie = categories.FirstOrDefault(c =>
                    string.Equals(c.INTITULE?.Trim(), cle, StringComparison.OrdinalIgnoreCase));
                if (categorie is null)
                {
                    lignesIgnores++;
                    continue;
                }
                pauseCategorie = categorie.PAUSE;
                var ids = affectations.Where(a => a.ID_CATEG == categorie.IDCATEGORIE).Select(a => a.MATRICULE_SAGE).ToHashSet();
                cibles = cards.Where(c => ids.Contains(c.ID)).ToList();
            }
            else
            {
                var carte = cards.FirstOrDefault(c => MemeIdentifiant(c.SAGE_MATRICULE, cle));
                cibles = carte is null ? new List<T_CARDPAIE>() : new List<T_CARDPAIE> { carte };
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
                    var entity = existantes.FirstOrDefault(p => p.IDSAL == carte.ID && p.DateP?.Date == jour);
                    if (entity is null)
                    {
                        entity = new T_PLANNING_SAL
                        {
                            IDSAL = carte.ID,
                            DateP = jour,
                            HA = ha,
                            HD = hd,
                            Pause = off ? TimeSpan.Zero : pause,
                            OffP = off,
                            BDD_SAGE = sageDb,
                            BDD_POINTEUSE = pointeuseDb
                        };
                        await repo.AddAsync(entity, ct);
                        existantes.Add(entity);
                    }
                    else
                    {
                        entity.DateP = jour;
                        entity.HA = ha;
                        entity.HD = hd;
                        entity.Pause = off ? TimeSpan.Zero : pause;
                        entity.OffP = off;
                        entity.BDD_SAGE = sageDb;
                        entity.BDD_POINTEUSE = pointeuseDb;
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
        var repo = _uow.Repository<T_PLANNING>();
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
            var entity = existing.FirstOrDefault(s => s.NoSHIFT == noShift);
            if (entity == null)
            {
                entity = new T_PLANNING { NoSHIFT = noShift, HA = ha == default ? null : ha, PAUSE = pause == default ? null : pause, HD = hd == default ? null : hd, Intitule = intitule };
                await repo.AddAsync(entity, ct);
                existing.Add(entity);
            }
            else
            {
                entity.HA = ha == default ? entity.HA : ha;
                entity.PAUSE = pause == default ? entity.PAUSE : pause;
                entity.HD = hd == default ? entity.HD : hd;
                if (!string.IsNullOrWhiteSpace(intitule))
                    entity.Intitule = intitule;
                repo.Update(entity);
            }
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        return new ImportResultDto(imported, skipped, "Shifts importés.");
    }

    private bool MatchesSage(string? stored)
    {
        var sage = _tenant.SageDb;
        if (string.IsNullOrWhiteSpace(stored))
            return true;
        return string.Equals(stored.Trim(), sage.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? Combine(DateTime date, TimeSpan? time)
        => time.HasValue ? date.Date + time.Value : date.Date;
}
