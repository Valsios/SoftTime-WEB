using System.Linq.Expressions;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public class TimekeepingService : ITimekeepingService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;
    private readonly IHistoryArchiveService _history;

    public TimekeepingService(IUnitOfWork uow, ITenantConnectionService tenant, IExternalSourceService externalSource, IHistoryArchiveService history)
    {
        _uow = uow;
        _tenant = tenant;
        _externalSource = externalSource;
        _history = history;
    }

    public async Task<IReadOnlyList<PunchDto>> ListPunchesAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<Pointage>().ListReadOnlyAsync(PunchPredicate(filter, _tenant.SageDb), ct);
        return list.Select(ToPunchDto).ToList();
    }

    /// <summary>
    /// Liste paginée des pointages. Le filtre, l'ordre et le découpage sont délégués à SQL
    /// (<c>COUNT(*)</c> + <c>ORDER BY … OFFSET/FETCH</c>) : seules les lignes de la page sont
    /// matérialisées. L'endpoint non paginé reste inchangé pour l'export Excel.
    /// </summary>
    public async Task<PagedResult<PunchDto>> ListPunchesPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<Pointage>(),
            PunchPredicate(filter, _tenant.SageDb),
            q => q.OrderBy(p => p.DatePointage)
                  .ThenBy(p => p.HeurePointage)
                  .ThenBy(p => p.MatriculeSage)
                  .ThenBy(p => p.IdPointage),
            page,
            pageSize,
            ToPunchDto,
            filter.Search,
            new Expression<Func<Pointage, string?>>[]
            {
                p => p.MatriculeSage,
                p => p.TypePointage,
            },
            ct);
    }

    private static PunchDto ToPunchDto(Pointage p) => new(
        p.IdPointage, p.MatriculeSage, p.DatePointage, p.HeurePointage, p.TypePointage, p.DateImportation);

    private static Expression<Func<Pointage, bool>> PunchPredicate(PeriodRequest filter, string sageDb)
    {
        var from = filter.From.Date;
        var toInclusive = filter.To.Date.AddDays(1);
        var matriculeFrom = filter.MatriculeFrom;
        var matriculeTo = filter.MatriculeTo;
        return p => p.NomBddSage == sageDb
            && p.DatePointage >= from
            && p.DatePointage <= toInclusive
            && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(p.MatriculeSage, matriculeFrom) >= 0)
            && (string.IsNullOrEmpty(matriculeTo) || string.Compare(p.MatriculeSage, matriculeTo) <= 0);
    }

    /// <summary>
    /// Corrige un pointage importé (date, heure, type). Refusé si la semaine est déjà validée.
    /// Le pré-traitement doit ensuite être relancé pour propager la correction.
    /// </summary>
    public async Task<PunchDto> UpdatePunchAsync(decimal id, PunchUpdateDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var repo = _uow.Repository<Pointage>();
        var entity = (await repo.ListAsync(p => p.IdPointage == id && p.NomBddSage == sageDb, ct)).FirstOrDefault()
            ?? throw new NotFoundException($"Pointage introuvable (ID={id}).");

        var jour = dto.Date?.Date ?? entity.DatePointage?.Date;
        await RefuserSiSemaineValideeAsync(sageDb, entity.MatriculeSage, jour, ct);

        if (dto.Date.HasValue)
            entity.DatePointage = dto.Date.Value.Date;
        if (dto.Heure.HasValue)
            entity.HeurePointage = dto.Heure.Value;
        if (dto.Type is not null)
            entity.TypePointage = string.IsNullOrWhiteSpace(dto.Type) ? null : dto.Type.Trim();

        repo.Update(entity);
        await _uow.SaveChangesAsync(ct);
        return new PunchDto(entity.IdPointage, entity.MatriculeSage, entity.DatePointage,
            entity.HeurePointage, entity.TypePointage, entity.DateImportation);
    }

    /// <summary>
    /// Purge une période de traitement : pointages (archivés), corrections automatiques et anomalies
    /// (archivées), pour la plage de matricules demandée. Portage de <c>Purger</c> du desktop, avec
    /// deux garde-fous : la période ne doit être ni validée ni déjà calculée en HS, et les
    /// corrections <b>manuelles</b> sont conservées (le desktop effaçait tout sans distinction).
    /// </summary>
    public async Task<PurgePunchesResultDto> PurgePunchesAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        // Garde indispensable : sans période explicite, FROM/TO valent DateTime.MinValue et la purge
        // porterait sur l'intégralité des pointages.
        if (request.From == default || request.To == default)
            throw new DomainException("La période (from/to) est requise pour purger.");
        if (request.To.Date < request.From.Date)
            throw new DomainException("La date de fin doit être postérieure ou égale à la date de début.");

        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var finExclusive = to.AddDays(1);

        var validees = (await _uow.Repository<HsValidee>().ListAsync(
            v => v.BddSage == sageDb && v.ValidationHs == true && v.DateDeb >= from && v.DateDeb <= to, ct)).Count;
        if (validees > 0)
            throw new DomainException(
                $"La période contient {validees} semaine(s) validée(s) : annulez la validation avant de purger.");

        var hsExistants = (await _uow.Repository<HsExoImp>().ListAsync(
            h => h.BddSage == sageDb && h.PeriodeDebut >= from && h.PeriodeFin <= to, ct)).Count;
        if (hsExistants > 0)
            throw new DomainException(
                "Des calculs EXO/IMPO existent sur la période : purgez les heures supplémentaires avant de purger les pointages.");

        var pointages = (await _uow.Repository<Pointage>().ListAsync(
                p => p.NomBddSage == sageDb && p.DatePointage >= from && p.DatePointage <= finExclusive, ct))
            .Where(p => MatriculeRange.Contains(p.MatriculeSage, request))
            .ToList();

        var corrRepo = _uow.Repository<HeureCorrigee>();
        var corrections = await corrRepo.ListAsync(
            h => h.BddSage == sageDb && h.DatePointageIn >= from && h.DatePointageIn <= finExclusive, ct);
        var correctionsCibles = corrections
            .Where(h => MatriculeRange.Contains(h.MatriculeSage, request)
                        && !string.Equals(h.Origine, PunchProcessingConfiguration.OrigineManuel, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var manuellesConservees = corrections.Count(h =>
            MatriculeRange.Contains(h.MatriculeSage, request)
            && string.Equals(h.Origine, PunchProcessingConfiguration.OrigineManuel, StringComparison.OrdinalIgnoreCase));

        var anomRepo = _uow.Repository<HeureAnomalie>();
        var anomalies = (await anomRepo.ListAsync(
                a => a.BddSage == sageDb && a.DatePointageIn >= from && a.DatePointageIn <= finExclusive, ct))
            .Where(a => MatriculeRange.Contains(a.MatriculeSage, request))
            .ToList();

        if (pointages.Count == 0 && correctionsCibles.Count == 0 && anomalies.Count == 0)
            return new PurgePunchesResultDto(0, 0, 0, manuellesConservees,
                manuellesConservees > 0
                    ? $"Aucune donnée à purger. {manuellesConservees} correction(s) manuelle(s) conservée(s)."
                    : "Aucune donnée à purger sur la période.");

        await _history.ArchivePunchesAsync(pointages, ct);
        await _history.ArchiveAnomaliesAsync(anomalies, ct);
        if (pointages.Count > 0)
            _uow.Repository<Pointage>().RemoveRange(pointages);
        if (correctionsCibles.Count > 0)
            corrRepo.RemoveRange(correctionsCibles);
        if (anomalies.Count > 0)
            anomRepo.RemoveRange(anomalies);
        await _uow.SaveChangesAsync(ct);

        var message = $"{pointages.Count} pointage(s), {correctionsCibles.Count} correction(s) automatique(s) "
            + $"et {anomalies.Count} anomalie(s) purgés.";
        if (manuellesConservees > 0)
            message += $" {manuellesConservees} correction(s) manuelle(s) conservée(s).";
        return new PurgePunchesResultDto(pointages.Count, correctionsCibles.Count, anomalies.Count, manuellesConservees, message);
    }

    /// <summary>Refuse toute modification rétroactive sur une semaine déjà validée.</summary>
    private async Task RefuserSiSemaineValideeAsync(string sageDb, string? matricule, DateTime? jour, CancellationToken ct)
    {
        if (matricule is null || jour is null)
            return;
        var lundi = jour.Value.Date.AddDays(-(((int)jour.Value.DayOfWeek + 6) % 7));
        var semaines = await _uow.Repository<HsValidee>().ListAsync(
            v => v.BddSage == sageDb && v.DateDeb == lundi && v.ValidationHs == true, ct);
        if (semaines.Any(v => MemeIdentifiant(v.Matricules, matricule)))
            throw new DomainException(
                $"La semaine du {lundi:dd/MM/yyyy} est validée : annulez la validation avant de modifier les pointages.");
    }

    /// <summary>
    /// Semaines validées de la base active, par (clé matricule, lundi) : chargées une fois pour les
    /// opérations de masse (import, validation d'anomalies) au lieu d'une requête par ligne.
    /// </summary>
    private async Task<HashSet<(string Cle, DateTime Lundi)>> ChargerSemainesValideesAsync(string sageDb, CancellationToken ct)
        => (await _uow.Repository<HsValidee>().ListAsync(
                v => v.BddSage == sageDb && v.ValidationHs == true && v.DateDeb != null, ct))
            .Where(v => !string.IsNullOrWhiteSpace(v.Matricules))
            .Select(v => (MatriculeIdentity.Cle(v.Matricules), v.DateDeb!.Value.Date))
            .ToHashSet();

    private static DateTime Lundi(DateTime jour) => jour.Date.AddDays(-(((int)jour.DayOfWeek + 6) % 7));

    private static bool AbsenceJourneeComplete(HeureAnomalie anomalie)
        => anomalie.AbsAm == true && anomalie.AbsPm == true;

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

    public async Task<ImportResultDto> ImportFromClockAsync(ImportPunchesRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        if (request.From == default || request.To == default)
            throw new DomainException("La période (from/to) est requise pour importer les pointages.");
        if (request.To.Date < request.From.Date)
            throw new DomainException("La date de fin doit être postérieure ou égale à la date de début.");

        _ = (await _uow.Repository<ParametrePointeuse>().ListAsync(_ => true, ct)).FirstOrDefault()
            ?? throw new DomainException("Paramètre pointeuse (T_CLOCK) manquant.");
        var pteRow = await _tenant.GetPointeuseRowAsync(ct);
        var cards = (await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == _tenant.SageDb, ct))
            .Where(c => MatchesPointeuse(c.PointeuseBdd, _tenant.PointeuseDb))
            .ToList();
        if (!string.IsNullOrEmpty(request.MatriculeFrom))
            cards = cards.Where(c => string.Compare(c.SageMatricule, request.MatriculeFrom, StringComparison.Ordinal) >= 0).ToList();
        if (!string.IsNullOrEmpty(request.MatriculeTo))
            cards = cards.Where(c => string.Compare(c.SageMatricule, request.MatriculeTo, StringComparison.Ordinal) <= 0).ToList();
        // Un meme badge peut etre associe a plusieurs correspondances paie : le rattachement est alors
        // ambigu, on ignore ce badge plutot que d'importer des pointages sur le mauvais salarie.
        var badgeMap = new Dictionary<string, CardPaie>(StringComparer.OrdinalIgnoreCase);
        var ambiguousBadges = new List<string>();
        foreach (var group in cards
                     .Where(c => !string.IsNullOrWhiteSpace(c.PointeuseNumero))
                     .GroupBy(c => c.PointeuseNumero!.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                ambiguousBadges.Add(group.Key);
                continue;
            }
            badgeMap[group.Key] = group.First();
        }
        var users = await _externalSource.GetPunchUsersAsync(pteRow, ct);
        var from = request.From.Date;
        var to = request.To.Date.AddDays(1);
        var punches = await _externalSource.GetPunchesAsync(pteRow, from, to, ct);
        var repo = _uow.Repository<Pointage>();
        var existing = await repo.ListAsync(p =>
            p.NomBddSage == _tenant.SageDb && p.DatePointage >= from && p.DatePointage < to, ct);
        // Dédoublonnage exact (matricule canonique + date + heure) dans les deux modes. L'ancien
        // comportement ne dédoublonnait qu'en mono : en multi, une réimportation doublait les lignes.
        var dejaImportes = existing
            .Where(p => MatchesPointeuse(p.NomBddPointeuse, _tenant.PointeuseDb))
            .Where(p => p.DatePointage.HasValue
                        && p.HeurePointage.HasValue
                        && !string.IsNullOrWhiteSpace(p.MatriculeSage))
            .Select(p => (
                Cle: MatriculeIdentity.Cle(p.MatriculeSage),
                Jour: p.DatePointage!.Value.Date,
                Heure: p.HeurePointage!.Value))
            .ToHashSet();
        var imported = 0;
        var skipped = 0;
        foreach (var punch in punches)
        {
            var user = users.FirstOrDefault(u => u.UserId == punch.UserId);
            if (user?.Badge is not string badge || !badgeMap.TryGetValue(badge.Trim(), out var card))
            { skipped++; continue; }
            if (!dejaImportes.Add((MatriculeIdentity.Cle(card.SageMatricule), punch.CheckTime.Date, punch.CheckTime.TimeOfDay)))
            { skipped++; continue; }
            await repo.AddAsync(new Pointage
            {
                NomBddSage = _tenant.SageDb,
                NomBddPointeuse = pteRow.NomBd,
                MatriculeSage = card.SageMatricule,
                DatePointage = punch.CheckTime.Date,
                HeurePointage = punch.CheckTime.TimeOfDay,
                DateImportation = DateTime.Now,
                TypePointage = string.IsNullOrWhiteSpace(punch.CheckType) ? pteRow.TypePointage : punch.CheckType,
                IdUniquePointage = punch.UserId
            }, ct);
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        var message = $"{imported} pointage(s) importé(s), {skipped} ignoré(s).";
        if (ambiguousBadges.Count > 0)
            message += $" {ambiguousBadges.Count} badge(s) associé(s) à plusieurs salariés ont été ignorés : "
                       + string.Join(", ", ambiguousBadges.Take(10))
                       + (ambiguousBadges.Count > 10 ? ", …" : string.Empty)
                       + ". Corrigez les correspondances paie en double pour importer ces pointages.";
        return new ImportResultDto(imported, skipped, message);
    }

    public async Task<ImportResultDto> ImportPunchesExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var parsed = new List<(string Matricule, DateTime Jour, TimeSpan Heure)>();
        var skipped = 0;
        foreach (var row in rows)
        {
            var matr = ExcelCells.Get(row, "Matricule", "MATRICULE", "Matricule SAGE");
            if (string.IsNullOrWhiteSpace(matr)) { skipped++; continue; }
            if (!ExcelCells.TryDate(ExcelCells.Get(row, "Date", "DATE", "Date pointage"), out var date))
            { skipped++; continue; }
            if (!ExcelCells.TryTime(ExcelCells.Get(row, "Heure", "HEURE", "Heure pointage"), out var heure))
            { skipped++; continue; }
            parsed.Add((matr.Trim(), date.Date, heure));
        }

        if (parsed.Count == 0)
            return new ImportResultDto(0, skipped,
                $"Aucun pointage valide : {skipped} ligne(s) ignorée(s) (matricule, date ou heure manquants).");

        var repo = _uow.Repository<Pointage>();
        var from = parsed.Min(p => p.Jour);
        var to = parsed.Max(p => p.Jour).AddDays(1);
        var existing = await repo.ListAsync(p =>
            p.NomBddSage == _tenant.SageDb
            && p.DatePointage >= from
            && p.DatePointage < to, ct);
        // Dédoublonnage contre la base et au sein du fichier (deux lignes identiques ne doivent
        // produire qu'un seul pointage).
        var dejaImportes = existing
            .Where(p => MatchesPointeuse(p.NomBddPointeuse, _tenant.PointeuseDb))
            .Where(p => p.DatePointage.HasValue
                        && p.HeurePointage.HasValue
                        && !string.IsNullOrWhiteSpace(p.MatriculeSage))
            .Select(p => (
                Cle: MatriculeIdentity.Cle(p.MatriculeSage),
                Jour: p.DatePointage!.Value.Date,
                Heure: p.HeurePointage!.Value))
            .ToHashSet();

        var imported = 0;
        foreach (var row in parsed)
        {
            if (!dejaImportes.Add((MatriculeIdentity.Cle(row.Matricule), row.Jour, row.Heure)))
            {
                skipped++;
                continue;
            }
            await repo.AddAsync(new Pointage
            {
                MatriculeSage = row.Matricule,
                DatePointage = row.Jour,
                HeurePointage = row.Heure,
                DateImportation = DateTime.Now,
                NomBddSage = _tenant.SageDb,
                NomBddPointeuse = _tenant.PointeuseDb,
                TypePointage = "IO"
            }, ct);
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        return new ImportResultDto(imported, skipped,
            $"{imported} pointage(s) importé(s), {skipped} ligne(s) ignorée(s).");
    }

    /// <summary>Filtre tenant pointeuse, en tolérant les lignes héritées sans base renseignée.</summary>
    private static bool MatchesPointeuse(string? valeur, string pointeuseDb) =>
        string.IsNullOrWhiteSpace(valeur)
        || string.Equals(valeur.Trim(), pointeuseDb.Trim(), StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<AnomalyDto>> ListAnomaliesAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        // Les anomalies sans date (importées sans date exploitable) ne doivent pas rester invisibles.
        var list = await _uow.Repository<HeureAnomalie>().ListReadOnlyAsync(AnomalyPredicate(filter, _tenant.SageDb), ct);
        return list.Select(MapAnomaly).ToList();
    }

    /// <summary>Liste paginée des anomalies ; les anomalies sans date restent incluses.</summary>
    public async Task<PagedResult<AnomalyDto>> ListAnomaliesPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<HeureAnomalie>(),
            AnomalyPredicate(filter, _tenant.SageDb),
            q => q.OrderBy(a => a.DatePointageIn)
                  .ThenBy(a => a.MatriculeSage)
                  .ThenBy(a => a.Id),
            page,
            pageSize,
            MapAnomaly,
            filter.Search,
            new Expression<Func<HeureAnomalie, string?>>[]
            {
                a => a.MatriculeSage,
                a => a.IntituleAbsence,
            },
            ct);
    }

    private static Expression<Func<HeureAnomalie, bool>> AnomalyPredicate(PeriodRequest filter, string sageDb)
    {
        var from = filter.From;
        var to = filter.To;
        return a => a.BddSage == sageDb
            && (a.DatePointageIn == null
                || (a.DatePointageIn >= from && a.DatePointageIn <= to));
    }

    public async Task<AnomalyDto?> GetAnomalyAsync(decimal id, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var entity = await LoadAnomalyAsync(id, ct);
        return entity is null ? null : MapAnomaly(entity);
    }

    /// <summary>
    /// Arbitre une anomalie : renseigne les heures retenues (entrée, pause, sortie) et les drapeaux
    /// d'absence/férié. Portage de <c>CorrectionFerie</c> + <c>CorrectionES</c> du desktop, réunis
    /// dans un seul endpoint (le desktop avait deux endpoints au comportement redondant).
    /// </summary>
    public async Task<AnomalyDto> UpdateAnomalyAsync(decimal id, AnomalyUpdateDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var entity = await LoadAnomalyAsync(id, ct)
            ?? throw new NotFoundException($"Anomalie introuvable (ID={id}).");

        var dateIn = dto.DateIn?.Date ?? entity.DatePointageIn?.Date;
        var dateOut = dto.DateOut?.Date ?? entity.DatePointageOut?.Date;
        var hEntree = dto.HEntree ?? entity.HEntree;
        var hPs = dto.HPs ?? entity.Hps;
        var hPe = dto.HPe ?? entity.Hpe;
        var hSortie = dto.HSortie ?? entity.HSortie;
        ValidateAnomalyHours(dateIn, hEntree, hPs, hPe, dateOut, hSortie);

        entity.DatePointageIn = dateIn;
        entity.DatePointageOut = dateOut;
        entity.HEntree = hEntree;
        entity.Hps = hPs;
        entity.Hpe = hPe;
        entity.HSortie = hSortie;
        if (dto.AbsAm.HasValue) entity.AbsAm = dto.AbsAm;
        if (dto.AbsPm.HasValue) entity.AbsPm = dto.AbsPm;
        if (dto.Feries.HasValue) entity.Feries = dto.Feries;
        if (dto.FeriesAm.HasValue) entity.FeriesAm = dto.FeriesAm;
        if (dto.FeriesPm.HasValue) entity.FeriesPm = dto.FeriesPm;
        if (dto.IntituleAbsence is not null)
            entity.IntituleAbsence = string.IsNullOrWhiteSpace(dto.IntituleAbsence) ? null : dto.IntituleAbsence.Trim();

        _uow.Repository<HeureAnomalie>().Update(entity);
        await _uow.SaveChangesAsync(ct);
        return MapAnomaly(entity);
    }

    /// <summary>
    /// Valide en masse : chaque anomalie sélectionnée devient une heure corrigée
    /// (<c>ORIGINE = 'MANUEL'</c>, <c>VALIDER_CORRECTION = true</c>) puis l'anomalie est supprimée.
    /// Portage de <c>ValiderCorrections</c> du desktop.
    /// </summary>
    public async Task<AnomalyValidationResultDto> ValidateAnomaliesAsync(
        IReadOnlyCollection<decimal> ids, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        if (ids is null || ids.Count == 0)
            throw new DomainException("Aucune anomalie sélectionnée.");

        var cible = ids.ToHashSet();
        var anomalies = (await _uow.Repository<HeureAnomalie>()
                .ListAsync(a => a.BddSage == _tenant.SageDb, ct))
            .Where(a => cible.Contains(a.Id))
            .ToList();
        if (anomalies.Count == 0)
            throw new DomainException("Aucune anomalie trouvée pour les identifiants fournis.");

        // Une absence de la journée complète (matin et après-midi) se valide sans heures : c'est
        // l'arbitrage « le salarié était absent ». Des heures saisies en plus seraient payées.
        var contradictoires = anomalies
            .Where(a => AbsenceJourneeComplete(a) && (a.HEntree.HasValue || a.HSortie.HasValue))
            .ToList();
        if (contradictoires.Count > 0)
        {
            var detail = string.Join(", ", contradictoires.Take(5)
                .Select(a => $"{a.MatriculeSage} du {a.DatePointageIn?.ToString("dd/MM/yyyy") ?? "?"}"));
            throw new DomainException(
                $"Absence de la journée complète et heures saisies pour : {detail}. Retirez les heures ou l'absence avant de valider.");
        }

        var incompletes = anomalies
            .Where(a => !AbsenceJourneeComplete(a) && (a.HEntree is null || a.HSortie is null))
            .ToList();
        if (incompletes.Count > 0)
        {
            var detail = string.Join(", ", incompletes.Take(5)
                .Select(a => $"{a.MatriculeSage} du {a.DatePointageIn?.ToString("dd/MM/yyyy") ?? "?"}"));
            throw new DomainException(
                $"Heure d'entrée ou de sortie manquante pour : {detail}. Renseignez-les avant de valider.");
        }

        var semainesValidees = await ChargerSemainesValideesAsync(_tenant.SageDb, ct);
        var verrouillees = anomalies
            .Where(a => a.DatePointageIn.HasValue
                        && semainesValidees.Contains((MatriculeIdentity.Cle(a.MatriculeSage), Lundi(a.DatePointageIn.Value))))
            .ToList();
        if (verrouillees.Count > 0)
        {
            var detail = string.Join(", ", verrouillees.Take(5)
                .Select(a => $"{a.MatriculeSage} du {a.DatePointageIn:dd/MM/yyyy}"));
            throw new DomainException(
                $"Semaine validée pour : {detail}. Annulez la validation avant de modifier les heures.");
        }

        var corrRepo = _uow.Repository<HeureCorrigee>();
        foreach (var anomalie in anomalies)
        {
            await corrRepo.AddAsync(new HeureCorrigee
            {
                BddSage = anomalie.BddSage,
                BddPointeuse = anomalie.BddPointeuse,
                MatriculeSage = anomalie.MatriculeSage,
                DatePointageIn = anomalie.DatePointageIn,
                DatePointageOut = anomalie.DatePointageOut,
                HEntree = anomalie.HEntree,
                Hps = anomalie.Hps,
                Hpe = anomalie.Hpe,
                HSortie = anomalie.HSortie,
                AbsenceAm = anomalie.AbsAm ?? false,
                AbsencePm = anomalie.AbsPm ?? false,
                Ferier = anomalie.Feries,
                FerierAm = anomalie.FeriesAm ?? false,
                FerierPm = anomalie.FeriesPm ?? false,
                IntituleAbsence = anomalie.IntituleAbsence,
                ValiderCorrection = true,
                Origine = PunchProcessingConfiguration.OrigineManuel
            }, ct);
        }
        _uow.Repository<HeureAnomalie>().RemoveRange(anomalies);
        await _history.ArchiveAnomaliesAsync(anomalies, ct);
        await _uow.SaveChangesAsync(ct);

        return new AnomalyValidationResultDto(anomalies.Count, anomalies.Count,
            $"{anomalies.Count} anomalie(s) convertie(s) en heure(s) corrigée(s).");
    }

    public async Task DeleteAnomalyAsync(decimal id, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<HeureAnomalie>();
        var e = await LoadAnomalyAsync(id, ct) ?? throw new NotFoundException();
        await _history.ArchiveAnomaliesAsync(new[] { e }, ct);
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>Charge une anomalie en la restreignant à la base SAGE du tenant courant.</summary>
    private async Task<HeureAnomalie?> LoadAnomalyAsync(decimal id, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        return (await _uow.Repository<HeureAnomalie>()
            .ListAsync(a => a.Id == id && a.BddSage == sageDb, ct)).FirstOrDefault();
    }

    /// <summary>
    /// Cohérence des heures arbitrées : la sortie doit être postérieure à l'entrée et, le même jour,
    /// les heures de pause doivent être comprises entre les deux (portage de la validation desktop).
    /// </summary>
    private static void ValidateAnomalyHours(
        DateTime? dateIn, TimeSpan? hEntree, TimeSpan? hPs, TimeSpan? hPe, DateTime? dateOut, TimeSpan? hSortie)
    {
        if (!dateIn.HasValue || !dateOut.HasValue || !hEntree.HasValue || !hSortie.HasValue)
            return;

        var entree = dateIn.Value.Date.Add(hEntree.Value);
        var sortie = dateOut.Value.Date.Add(hSortie.Value);
        if (sortie <= entree)
            throw new DomainException("La sortie doit être postérieure à l'entrée.");

        if (dateIn.Value.Date != dateOut.Value.Date)
            return;
        foreach (var pause in new[] { hPs, hPe }.Where(h => h.HasValue).Select(h => h!.Value))
        {
            var t = dateIn.Value.Date.Add(pause);
            if (t < entree || t > sortie)
                throw new DomainException(
                    "Les heures de pause doivent être comprises entre l'entrée et la sortie.");
        }
    }

    private static AnomalyDto MapAnomaly(HeureAnomalie a) => new(
        a.Id, a.MatriculeSage, a.DatePointageIn, a.DatePointageOut, a.H1, a.H2,
        a.HEntree, a.Hps, a.Hpe, a.HSortie,
        a.AbsAm, a.AbsPm, a.Feries, a.FeriesAm, a.FeriesPm, a.IntituleAbsence);

    public async Task<IReadOnlyList<CorrectedHourDto>> ListCorrectionsAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<HeureCorrigee>().ListReadOnlyAsync(CorrectionPredicate(filter, _tenant.SageDb), ct);
        return list.Select(MapCorrection).ToList();
    }

    /// <summary>
    /// Liste paginée des heures corrigées (pagination SQL, voir <see cref="ListPunchesPagedAsync"/> :
    /// l'endpoint non paginé reste inchangé pour ne pas tronquer l'export Excel du client).
    /// </summary>
    public async Task<PagedResult<CorrectedHourDto>> ListCorrectionsPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<HeureCorrigee>(),
            CorrectionPredicate(filter, _tenant.SageDb),
            q => q.OrderBy(a => a.DatePointageIn)
                  .ThenBy(a => a.MatriculeSage)
                  .ThenBy(a => a.Id),
            page,
            pageSize,
            MapCorrection,
            filter.Search,
            new Expression<Func<HeureCorrigee, string?>>[]
            {
                a => a.MatriculeSage,
                a => a.IntituleAbsence,
            },
            ct);
    }

    private static Expression<Func<HeureCorrigee, bool>> CorrectionPredicate(PeriodRequest filter, string sageDb)
    {
        var from = filter.From;
        var to = filter.To;
        var matriculeFrom = filter.MatriculeFrom;
        var matriculeTo = filter.MatriculeTo;
        return a => a.BddSage == sageDb
            && a.DatePointageIn >= from
            && a.DatePointageIn <= to
            && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(a.MatriculeSage, matriculeFrom) >= 0)
            && (string.IsNullOrEmpty(matriculeTo) || string.Compare(a.MatriculeSage, matriculeTo) <= 0);
    }

    public async Task<CorrectedHourDto> SaveCorrectionAsync(CorrectedHourDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<HeureCorrigee>();
        var sageDb = _tenant.SageDb;
        // La semaine cible ne doit pas être validée (ni, pour une modification, la semaine d'origine).
        await RefuserSiSemaineValideeAsync(sageDb, dto.Matricule, dto.DateIn, ct);
        HeureCorrigee entity;
        if (dto.Id == 0)
        {
            entity = new HeureCorrigee
            {
                BddSage = _tenant.SageDb,
                BddPointeuse = _tenant.PointeuseDb,
                MatriculeSage = dto.Matricule,
                // Une saisie manuelle est protégée du pré-traitement des pointages.
                Origine = PunchProcessingConfiguration.OrigineManuel
            };
            Apply(dto, entity);
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
            await RefuserSiSemaineValideeAsync(sageDb, entity.MatriculeSage, entity.DatePointageIn, ct);
            entity.Origine = PunchProcessingConfiguration.OrigineManuel;
            Apply(dto, entity);
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return MapCorrection(entity);
    }

    public async Task DeleteCorrectionAsync(decimal id, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<HeureCorrigee>();
        var sageDb = _tenant.SageDb;
        var e = (await repo.ListAsync(h => h.Id == id && h.BddSage == sageDb, ct)).FirstOrDefault()
                ?? throw new NotFoundException();
        await RefuserSiSemaineValideeAsync(sageDb, e.MatriculeSage, e.DatePointageIn, ct);
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ImportResultDto> ImportCorrectionsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<HeureCorrigee>();
        var semainesValidees = await ChargerSemainesValideesAsync(_tenant.SageDb, ct);
        var imported = 0;
        var sansMatricule = 0;
        var verrouillees = 0;
        foreach (var row in rows)
        {
            var matr = ExcelCells.Get(row, "Matricule", "MATRICULE", "Matricule SAGE");
            if (string.IsNullOrWhiteSpace(matr))
            {
                sansMatricule++;
                continue;
            }
            ExcelCells.TryDate(ExcelCells.Get(row, "DateEntree", "DATE ENTREE", "Date entrée", "Date in"), out var dIn);
            ExcelCells.TryDate(ExcelCells.Get(row, "DateSortie", "DATE SORTIE", "Date sortie", "Date out"), out var dOut);
            ExcelCells.TryTime(ExcelCells.Get(row, "HeureEntree", "HEURE ENTREE", "Heure entrée"), out var hIn);
            ExcelCells.TryTime(ExcelCells.Get(row, "HeureSortie", "HEURE SORTIE", "Heure sortie"), out var hOut);
            // Une semaine validée est figée : la ligne est ignorée plutôt que d'en changer les heures.
            if (dIn != default && semainesValidees.Contains((MatriculeIdentity.Cle(matr), Lundi(dIn))))
            {
                verrouillees++;
                continue;
            }
            await repo.AddAsync(new HeureCorrigee
            {
                MatriculeSage = matr,
                DatePointageIn = dIn == default ? null : dIn,
                DatePointageOut = dOut == default ? null : dOut,
                HEntree = hIn,
                HSortie = hOut,
                BddSage = _tenant.SageDb,
                BddPointeuse = _tenant.PointeuseDb,
                ValiderCorrection = true,
                // Une correction importée est protégée du pré-traitement des pointages.
                Origine = PunchProcessingConfiguration.OrigineManuel
            }, ct);
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        var skipped = sansMatricule + verrouillees;
        var message = $"{imported} correction(s) importée(s), {skipped} ligne(s) ignorée(s)";
        var motifs = new List<string>();
        if (sansMatricule > 0)
            motifs.Add("matricule manquant");
        if (verrouillees > 0)
            motifs.Add($"{verrouillees} sur une semaine validée");
        if (motifs.Count > 0)
            message += $" ({string.Join(", ", motifs)})";
        return new ImportResultDto(imported, skipped, message + ".");
    }

    private static void Apply(CorrectedHourDto dto, HeureCorrigee e)
    {
        e.MatriculeSage = dto.Matricule;
        e.DatePointageIn = dto.DateIn;
        e.DatePointageOut = dto.DateOut;
        e.HEntree = dto.HEntree;
        e.Hps = dto.HPs;
        e.Hpe = dto.HPe;
        e.HSortie = dto.HSortie;
        e.AbsenceAm = dto.AbsAm;
        e.AbsencePm = dto.AbsPm;
        e.IntituleAbsence = dto.IntituleAbsence;
        e.Retard = dto.Retard;
        e.Ferier = dto.Ferie;
        e.FerierAm = dto.FerieAm;
        e.FerierPm = dto.FeriePm;
        e.ValiderCorrection = dto.ValiderCorrection;
        e.Hs = dto.Hs;
        e.MNuit = dto.MNuit;
        e.MDimanche = dto.MDimanche;
        e.MFeries = dto.MFeries;
    }

    private static CorrectedHourDto MapCorrection(HeureCorrigee a) => new(
        a.Id, a.MatriculeSage, a.DatePointageIn, a.DatePointageOut, a.HEntree, a.Hps, a.Hpe, a.HSortie,
        a.AbsenceAm, a.AbsencePm, a.IntituleAbsence, a.Retard, a.Ferier, a.FerierAm, a.FerierPm,
        a.ValiderCorrection, a.ValiderHs, a.Hs, a.MNuit, a.MDimanche, a.MFeries);
}
