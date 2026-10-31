using System.Linq.Expressions;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

public class TimekeepingService
{
    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;
    private readonly HistoryArchiveService _history;

    public TimekeepingService(IUnitOfWork uow, TenantConnectionService tenant, IExternalSourceService externalSource, HistoryArchiveService history)
    {
        _uow = uow;
        _tenant = tenant;
        _externalSource = externalSource;
        _history = history;
    }

    public async Task<IReadOnlyList<PunchDto>> ListPunchesAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<T_POINTAGE>().ListAsync(PunchPredicate(filter, _tenant.SageDb), ct);
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
            _uow.Repository<T_POINTAGE>(),
            PunchPredicate(filter, _tenant.SageDb),
            q => q.OrderBy(p => p.DATE_POINTAGE)
                  .ThenBy(p => p.HEURE_POINTAGE)
                  .ThenBy(p => p.MATRICULE_SAGE)
                  .ThenBy(p => p.IDPOINTAGE),
            page,
            pageSize,
            ToPunchDto,
            filter.Search,
            new Expression<Func<T_POINTAGE, string?>>[]
            {
                p => p.MATRICULE_SAGE,
                p => p.TYPE_POINTAGE,
            },
            ct);
    }

    private static PunchDto ToPunchDto(T_POINTAGE p) => new(
        p.IDPOINTAGE, p.MATRICULE_SAGE, p.DATE_POINTAGE, p.HEURE_POINTAGE, p.TYPE_POINTAGE, p.DATE_IMPORTATION);

    private static Expression<Func<T_POINTAGE, bool>> PunchPredicate(PeriodRequest filter, string sageDb)
    {
        var from = filter.From.Date;
        var toInclusive = filter.To.Date.AddDays(1);
        var matriculeFrom = filter.MatriculeFrom;
        var matriculeTo = filter.MatriculeTo;
        return p => p.NOM_BDD_SAGE == sageDb
            && p.DATE_POINTAGE >= from
            && p.DATE_POINTAGE <= toInclusive
            && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(p.MATRICULE_SAGE, matriculeFrom) >= 0)
            && (string.IsNullOrEmpty(matriculeTo) || string.Compare(p.MATRICULE_SAGE, matriculeTo) <= 0);
    }

    /// <summary>
    /// Corrige un pointage importé (date, heure, type). Refusé si la semaine est déjà validée.
    /// Le pré-traitement doit ensuite être relancé pour propager la correction.
    /// </summary>
    public async Task<PunchDto> UpdatePunchAsync(decimal id, PunchUpdateDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageDb = _tenant.SageDb;
        var repo = _uow.Repository<T_POINTAGE>();
        var entity = (await repo.ListAsync(p => p.IDPOINTAGE == id && p.NOM_BDD_SAGE == sageDb, ct)).FirstOrDefault()
            ?? throw new KeyNotFoundException($"Pointage introuvable (ID={id}).");

        var jour = dto.Date?.Date ?? entity.DATE_POINTAGE?.Date;
        await RefuserSiSemaineValideeAsync(sageDb, entity.MATRICULE_SAGE, jour, ct);

        if (dto.Date.HasValue)
            entity.DATE_POINTAGE = dto.Date.Value.Date;
        if (dto.Heure.HasValue)
            entity.HEURE_POINTAGE = dto.Heure.Value;
        if (dto.Type is not null)
            entity.TYPE_POINTAGE = string.IsNullOrWhiteSpace(dto.Type) ? null : dto.Type.Trim();

        repo.Update(entity);
        await _uow.SaveChangesAsync(ct);
        return new PunchDto(entity.IDPOINTAGE, entity.MATRICULE_SAGE, entity.DATE_POINTAGE,
            entity.HEURE_POINTAGE, entity.TYPE_POINTAGE, entity.DATE_IMPORTATION);
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
            throw new InvalidOperationException("La période (from/to) est requise pour purger.");
        if (request.To.Date < request.From.Date)
            throw new InvalidOperationException("La date de fin doit être postérieure ou égale à la date de début.");

        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var finExclusive = to.AddDays(1);

        var validees = (await _uow.Repository<T_HS_VALIDER>().ListAsync(
            v => v.BDD_SAGE == sageDb && v.VALIDATION_HS == true && v.DATE_DEB >= from && v.DATE_DEB <= to, ct)).Count;
        if (validees > 0)
            throw new InvalidOperationException(
                $"La période contient {validees} semaine(s) validée(s) : annulez la validation avant de purger.");

        var hsExistants = (await _uow.Repository<T_HSExoImp>().ListAsync(
            h => h.BDD_SAGE == sageDb && h.PeriodeDebut >= from && h.PeriodeFin <= to, ct)).Count;
        if (hsExistants > 0)
            throw new InvalidOperationException(
                "Des calculs EXO/IMPO existent sur la période : purgez les heures supplémentaires avant de purger les pointages.");

        var pointages = (await _uow.Repository<T_POINTAGE>().ListAsync(
                p => p.NOM_BDD_SAGE == sageDb && p.DATE_POINTAGE >= from && p.DATE_POINTAGE <= finExclusive, ct))
            .Where(p => MatriculeRange.Contains(p.MATRICULE_SAGE, request))
            .ToList();

        var corrRepo = _uow.Repository<T_HEURECORRIGER>();
        var corrections = await corrRepo.ListAsync(
            h => h.BDD_SAGE == sageDb && h.DATE_POINTAGE_IN >= from && h.DATE_POINTAGE_IN <= finExclusive, ct);
        var correctionsCibles = corrections
            .Where(h => MatriculeRange.Contains(h.MATRICULE_SAGE, request)
                        && !string.Equals(h.ORIGINE, PunchProcessingConfiguration.OrigineManuel, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var manuellesConservees = corrections.Count(h =>
            MatriculeRange.Contains(h.MATRICULE_SAGE, request)
            && string.Equals(h.ORIGINE, PunchProcessingConfiguration.OrigineManuel, StringComparison.OrdinalIgnoreCase));

        var anomRepo = _uow.Repository<T_HEUREANOMALIE>();
        var anomalies = (await anomRepo.ListAsync(
                a => a.BDD_SAGE == sageDb && a.DATE_POINTAGE_IN >= from && a.DATE_POINTAGE_IN <= finExclusive, ct))
            .Where(a => MatriculeRange.Contains(a.MATRICULE_SAGE, request))
            .ToList();

        if (pointages.Count == 0 && correctionsCibles.Count == 0 && anomalies.Count == 0)
            return new PurgePunchesResultDto(0, 0, 0, manuellesConservees,
                manuellesConservees > 0
                    ? $"Aucune donnée à purger. {manuellesConservees} correction(s) manuelle(s) conservée(s)."
                    : "Aucune donnée à purger sur la période.");

        await _history.ArchivePunchesAsync(pointages, ct);
        await _history.ArchiveAnomaliesAsync(anomalies, ct);
        if (pointages.Count > 0)
            _uow.Repository<T_POINTAGE>().RemoveRange(pointages);
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
        var semaines = await _uow.Repository<T_HS_VALIDER>().ListAsync(
            v => v.BDD_SAGE == sageDb && v.DATE_DEB == lundi && v.VALIDATION_HS == true, ct);
        if (semaines.Any(v => MemeIdentifiant(v.MATRICULES, matricule)))
            throw new InvalidOperationException(
                $"La semaine du {lundi:dd/MM/yyyy} est validée : annulez la validation avant de modifier les pointages.");
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

    public async Task<ImportResultDto> ImportFromClockAsync(ImportPunchesRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        if (request.From == default || request.To == default)
            throw new InvalidOperationException("La période (from/to) est requise pour importer les pointages.");
        if (request.To.Date < request.From.Date)
            throw new InvalidOperationException("La date de fin doit être postérieure ou égale à la date de début.");

        _ = (await _uow.Repository<T_CLOCK>().ListAsync(_ => true, ct)).FirstOrDefault()
            ?? throw new InvalidOperationException("Paramètre pointeuse (T_CLOCK) manquant.");
        var pteRow = await _tenant.GetPointeuseRowAsync(ct);
        var cards = (await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == _tenant.SageDb, ct))
            .Where(c => MatchesPointeuse(c.POINTEUSE_BDD, _tenant.PointeuseDb))
            .ToList();
        if (!string.IsNullOrEmpty(request.MatriculeFrom))
            cards = cards.Where(c => string.Compare(c.SAGE_MATRICULE, request.MatriculeFrom, StringComparison.Ordinal) >= 0).ToList();
        if (!string.IsNullOrEmpty(request.MatriculeTo))
            cards = cards.Where(c => string.Compare(c.SAGE_MATRICULE, request.MatriculeTo, StringComparison.Ordinal) <= 0).ToList();
        // Un meme badge peut etre associe a plusieurs correspondances paie : le rattachement est alors
        // ambigu, on ignore ce badge plutot que d'importer des pointages sur le mauvais salarie.
        var badgeMap = new Dictionary<string, T_CARDPAIE>(StringComparer.OrdinalIgnoreCase);
        var ambiguousBadges = new List<string>();
        foreach (var group in cards
                     .Where(c => !string.IsNullOrWhiteSpace(c.POINTEUSE_NUMERO))
                     .GroupBy(c => c.POINTEUSE_NUMERO!.Trim(), StringComparer.OrdinalIgnoreCase))
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
        var repo = _uow.Repository<T_POINTAGE>();
        var existing = await repo.ListAsync(p =>
            p.NOM_BDD_SAGE == _tenant.SageDb && p.DATE_POINTAGE >= from && p.DATE_POINTAGE < to, ct);
        // Dédoublonnage exact (matricule canonique + date + heure) dans les deux modes. L'ancien
        // comportement ne dédoublonnait qu'en mono : en multi, une réimportation doublait les lignes.
        var dejaImportes = existing
            .Where(p => MatchesPointeuse(p.NOM_BDD_POINTEUSE, _tenant.PointeuseDb))
            .Where(p => p.DATE_POINTAGE.HasValue
                        && p.HEURE_POINTAGE.HasValue
                        && !string.IsNullOrWhiteSpace(p.MATRICULE_SAGE))
            .Select(p => (
                Cle: MatriculeIdentity.Cle(p.MATRICULE_SAGE),
                Jour: p.DATE_POINTAGE!.Value.Date,
                Heure: p.HEURE_POINTAGE!.Value))
            .ToHashSet();
        var imported = 0;
        var skipped = 0;
        foreach (var punch in punches)
        {
            var user = users.FirstOrDefault(u => u.UserId == punch.UserId);
            if (user?.Badge is not string badge || !badgeMap.TryGetValue(badge.Trim(), out var card))
            { skipped++; continue; }
            if (!dejaImportes.Add((MatriculeIdentity.Cle(card.SAGE_MATRICULE), punch.CheckTime.Date, punch.CheckTime.TimeOfDay)))
            { skipped++; continue; }
            await repo.AddAsync(new T_POINTAGE
            {
                NOM_BDD_SAGE = _tenant.SageDb,
                NOM_BDD_POINTEUSE = pteRow.NOM_BD,
                MATRICULE_SAGE = card.SAGE_MATRICULE,
                DATE_POINTAGE = punch.CheckTime.Date,
                HEURE_POINTAGE = punch.CheckTime.TimeOfDay,
                DATE_IMPORTATION = DateTime.Now,
                TYPE_POINTAGE = string.IsNullOrWhiteSpace(punch.CheckType) ? pteRow.TYPE_POINTAGE : punch.CheckType,
                IDUNIQUE_POINTAGE = punch.UserId
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

        var repo = _uow.Repository<T_POINTAGE>();
        var from = parsed.Min(p => p.Jour);
        var to = parsed.Max(p => p.Jour).AddDays(1);
        var existing = await repo.ListAsync(p =>
            p.NOM_BDD_SAGE == _tenant.SageDb
            && p.DATE_POINTAGE >= from
            && p.DATE_POINTAGE < to, ct);
        // Dédoublonnage contre la base et au sein du fichier (deux lignes identiques ne doivent
        // produire qu'un seul pointage).
        var dejaImportes = existing
            .Where(p => MatchesPointeuse(p.NOM_BDD_POINTEUSE, _tenant.PointeuseDb))
            .Where(p => p.DATE_POINTAGE.HasValue
                        && p.HEURE_POINTAGE.HasValue
                        && !string.IsNullOrWhiteSpace(p.MATRICULE_SAGE))
            .Select(p => (
                Cle: MatriculeIdentity.Cle(p.MATRICULE_SAGE),
                Jour: p.DATE_POINTAGE!.Value.Date,
                Heure: p.HEURE_POINTAGE!.Value))
            .ToHashSet();

        var imported = 0;
        foreach (var row in parsed)
        {
            if (!dejaImportes.Add((MatriculeIdentity.Cle(row.Matricule), row.Jour, row.Heure)))
            {
                skipped++;
                continue;
            }
            await repo.AddAsync(new T_POINTAGE
            {
                MATRICULE_SAGE = row.Matricule,
                DATE_POINTAGE = row.Jour,
                HEURE_POINTAGE = row.Heure,
                DATE_IMPORTATION = DateTime.Now,
                NOM_BDD_SAGE = _tenant.SageDb,
                NOM_BDD_POINTEUSE = _tenant.PointeuseDb,
                TYPE_POINTAGE = "IO"
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
        var list = await _uow.Repository<T_HEUREANOMALIE>().ListAsync(AnomalyPredicate(filter, _tenant.SageDb), ct);
        return list.Select(MapAnomaly).ToList();
    }

    /// <summary>Liste paginée des anomalies ; les anomalies sans date restent incluses.</summary>
    public async Task<PagedResult<AnomalyDto>> ListAnomaliesPagedAsync(PeriodRequest filter, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        return await PagedLists.PageAsync(
            _uow.Repository<T_HEUREANOMALIE>(),
            AnomalyPredicate(filter, _tenant.SageDb),
            q => q.OrderBy(a => a.DATE_POINTAGE_IN)
                  .ThenBy(a => a.MATRICULE_SAGE)
                  .ThenBy(a => a.ID),
            page,
            pageSize,
            MapAnomaly,
            filter.Search,
            new Expression<Func<T_HEUREANOMALIE, string?>>[]
            {
                a => a.MATRICULE_SAGE,
                a => a.INTITULE_ABSENCE,
            },
            ct);
    }

    private static Expression<Func<T_HEUREANOMALIE, bool>> AnomalyPredicate(PeriodRequest filter, string sageDb)
    {
        var from = filter.From;
        var to = filter.To;
        return a => a.BDD_SAGE == sageDb
            && (a.DATE_POINTAGE_IN == null
                || (a.DATE_POINTAGE_IN >= from && a.DATE_POINTAGE_IN <= to));
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
            ?? throw new KeyNotFoundException($"Anomalie introuvable (ID={id}).");

        var dateIn = dto.DateIn?.Date ?? entity.DATE_POINTAGE_IN?.Date;
        var dateOut = dto.DateOut?.Date ?? entity.DATE_POINTAGE_OUT?.Date;
        var hEntree = dto.HEntree ?? entity.HENTREE;
        var hPs = dto.HPs ?? entity.HPS;
        var hPe = dto.HPe ?? entity.HPE;
        var hSortie = dto.HSortie ?? entity.HSORTIE;
        ValidateAnomalyHours(dateIn, hEntree, hPs, hPe, dateOut, hSortie);

        entity.DATE_POINTAGE_IN = dateIn;
        entity.DATE_POINTAGE_OUT = dateOut;
        entity.HENTREE = hEntree;
        entity.HPS = hPs;
        entity.HPE = hPe;
        entity.HSORTIE = hSortie;
        if (dto.AbsAm.HasValue) entity.ABS_AM = dto.AbsAm;
        if (dto.AbsPm.HasValue) entity.ABS_PM = dto.AbsPm;
        if (dto.Feries.HasValue) entity.FERIES = dto.Feries;
        if (dto.FeriesAm.HasValue) entity.FERIES_AM = dto.FeriesAm;
        if (dto.FeriesPm.HasValue) entity.FERIES_PM = dto.FeriesPm;
        if (dto.IntituleAbsence is not null)
            entity.INTITULE_ABSENCE = string.IsNullOrWhiteSpace(dto.IntituleAbsence) ? null : dto.IntituleAbsence.Trim();

        _uow.Repository<T_HEUREANOMALIE>().Update(entity);
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
            throw new InvalidOperationException("Aucune anomalie sélectionnée.");

        var cible = ids.ToHashSet();
        var anomalies = (await _uow.Repository<T_HEUREANOMALIE>()
                .ListAsync(a => a.BDD_SAGE == _tenant.SageDb, ct))
            .Where(a => cible.Contains(a.ID))
            .ToList();
        if (anomalies.Count == 0)
            throw new InvalidOperationException("Aucune anomalie trouvée pour les identifiants fournis.");

        var incompletes = anomalies.Where(a => a.HENTREE is null || a.HSORTIE is null).ToList();
        if (incompletes.Count > 0)
        {
            var detail = string.Join(", ", incompletes.Take(5)
                .Select(a => $"{a.MATRICULE_SAGE} du {a.DATE_POINTAGE_IN?.ToString("dd/MM/yyyy") ?? "?"}"));
            throw new InvalidOperationException(
                $"Heure d'entrée ou de sortie manquante pour : {detail}. Renseignez-les avant de valider.");
        }

        var corrRepo = _uow.Repository<T_HEURECORRIGER>();
        foreach (var anomalie in anomalies)
        {
            await corrRepo.AddAsync(new T_HEURECORRIGER
            {
                BDD_SAGE = anomalie.BDD_SAGE,
                BDD_POINTEUSE = anomalie.BDD_POINTEUSE,
                MATRICULE_SAGE = anomalie.MATRICULE_SAGE,
                DATE_POINTAGE_IN = anomalie.DATE_POINTAGE_IN,
                DATE_POINTAGE_OUT = anomalie.DATE_POINTAGE_OUT,
                HENTREE = anomalie.HENTREE,
                HPS = anomalie.HPS,
                HPE = anomalie.HPE,
                HSORTIE = anomalie.HSORTIE,
                ABSENCE_AM = anomalie.ABS_AM ?? false,
                ABSENCE_PM = anomalie.ABS_PM ?? false,
                FERIER = anomalie.FERIES,
                FERIER_AM = anomalie.FERIES_AM ?? false,
                FERIER_PM = anomalie.FERIES_PM ?? false,
                INTITULE_ABSENCE = anomalie.INTITULE_ABSENCE,
                VALIDER_CORRECTION = true,
                ORIGINE = PunchProcessingConfiguration.OrigineManuel
            }, ct);
        }
        _uow.Repository<T_HEUREANOMALIE>().RemoveRange(anomalies);
        await _history.ArchiveAnomaliesAsync(anomalies, ct);
        await _uow.SaveChangesAsync(ct);

        return new AnomalyValidationResultDto(anomalies.Count, anomalies.Count,
            $"{anomalies.Count} anomalie(s) convertie(s) en heure(s) corrigée(s).");
    }

    public async Task DeleteAnomalyAsync(decimal id, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<T_HEUREANOMALIE>();
        var e = await LoadAnomalyAsync(id, ct) ?? throw new KeyNotFoundException();
        await _history.ArchiveAnomaliesAsync(new[] { e }, ct);
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>Charge une anomalie en la restreignant à la base SAGE du tenant courant.</summary>
    private async Task<T_HEUREANOMALIE?> LoadAnomalyAsync(decimal id, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        return (await _uow.Repository<T_HEUREANOMALIE>()
            .ListAsync(a => a.ID == id && a.BDD_SAGE == sageDb, ct)).FirstOrDefault();
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
            throw new InvalidOperationException("La sortie doit être postérieure à l'entrée.");

        if (dateIn.Value.Date != dateOut.Value.Date)
            return;
        foreach (var pause in new[] { hPs, hPe }.Where(h => h.HasValue).Select(h => h!.Value))
        {
            var t = dateIn.Value.Date.Add(pause);
            if (t < entree || t > sortie)
                throw new InvalidOperationException(
                    "Les heures de pause doivent être comprises entre l'entrée et la sortie.");
        }
    }

    private static AnomalyDto MapAnomaly(T_HEUREANOMALIE a) => new(
        a.ID, a.MATRICULE_SAGE, a.DATE_POINTAGE_IN, a.DATE_POINTAGE_OUT, a.H1, a.H2,
        a.HENTREE, a.HPS, a.HPE, a.HSORTIE,
        a.ABS_AM, a.ABS_PM, a.FERIES, a.FERIES_AM, a.FERIES_PM, a.INTITULE_ABSENCE);

    public async Task<IReadOnlyList<CorrectedHourDto>> ListCorrectionsAsync(PeriodRequest filter, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var list = await _uow.Repository<T_HEURECORRIGER>().ListAsync(CorrectionPredicate(filter, _tenant.SageDb), ct);
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
            _uow.Repository<T_HEURECORRIGER>(),
            CorrectionPredicate(filter, _tenant.SageDb),
            q => q.OrderBy(a => a.DATE_POINTAGE_IN)
                  .ThenBy(a => a.MATRICULE_SAGE)
                  .ThenBy(a => a.ID),
            page,
            pageSize,
            MapCorrection,
            filter.Search,
            new Expression<Func<T_HEURECORRIGER, string?>>[]
            {
                a => a.MATRICULE_SAGE,
                a => a.INTITULE_ABSENCE,
            },
            ct);
    }

    private static Expression<Func<T_HEURECORRIGER, bool>> CorrectionPredicate(PeriodRequest filter, string sageDb)
    {
        var from = filter.From;
        var to = filter.To;
        var matriculeFrom = filter.MatriculeFrom;
        var matriculeTo = filter.MatriculeTo;
        return a => a.BDD_SAGE == sageDb
            && a.DATE_POINTAGE_IN >= from
            && a.DATE_POINTAGE_IN <= to
            && (string.IsNullOrEmpty(matriculeFrom) || string.Compare(a.MATRICULE_SAGE, matriculeFrom) >= 0)
            && (string.IsNullOrEmpty(matriculeTo) || string.Compare(a.MATRICULE_SAGE, matriculeTo) <= 0);
    }

    public async Task<CorrectedHourDto> SaveCorrectionAsync(CorrectedHourDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<T_HEURECORRIGER>();
        T_HEURECORRIGER entity;
        if (dto.Id == 0)
        {
            entity = new T_HEURECORRIGER
            {
                BDD_SAGE = _tenant.SageDb,
                BDD_POINTEUSE = _tenant.PointeuseDb,
                MATRICULE_SAGE = dto.Matricule,
                // Une saisie manuelle est protégée du pré-traitement des pointages.
                ORIGINE = PunchProcessingConfiguration.OrigineManuel
            };
            Apply(dto, entity);
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.ORIGINE = PunchProcessingConfiguration.OrigineManuel;
            Apply(dto, entity);
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return MapCorrection(entity);
    }

    public async Task DeleteCorrectionAsync(decimal id, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<T_HEURECORRIGER>();
        var sageDb = _tenant.SageDb;
        var e = (await repo.ListAsync(h => h.ID == id && h.BDD_SAGE == sageDb, ct)).FirstOrDefault()
                ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ImportResultDto> ImportCorrectionsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<T_HEURECORRIGER>();
        var imported = 0;
        var skipped = 0;
        foreach (var row in rows)
        {
            var matr = ExcelCells.Get(row, "Matricule", "MATRICULE", "Matricule SAGE");
            if (string.IsNullOrWhiteSpace(matr))
            {
                skipped++;
                continue;
            }
            ExcelCells.TryDate(ExcelCells.Get(row, "DateEntree", "DATE ENTREE", "Date entrée", "Date in"), out var dIn);
            ExcelCells.TryDate(ExcelCells.Get(row, "DateSortie", "DATE SORTIE", "Date sortie", "Date out"), out var dOut);
            ExcelCells.TryTime(ExcelCells.Get(row, "HeureEntree", "HEURE ENTREE", "Heure entrée"), out var hIn);
            ExcelCells.TryTime(ExcelCells.Get(row, "HeureSortie", "HEURE SORTIE", "Heure sortie"), out var hOut);
            await repo.AddAsync(new T_HEURECORRIGER
            {
                MATRICULE_SAGE = matr,
                DATE_POINTAGE_IN = dIn == default ? null : dIn,
                DATE_POINTAGE_OUT = dOut == default ? null : dOut,
                HENTREE = hIn,
                HSORTIE = hOut,
                BDD_SAGE = _tenant.SageDb,
                BDD_POINTEUSE = _tenant.PointeuseDb,
                VALIDER_CORRECTION = true,
                // Une correction importée est protégée du pré-traitement des pointages.
                ORIGINE = PunchProcessingConfiguration.OrigineManuel
            }, ct);
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        var message = $"{imported} correction(s) importée(s), {skipped} ligne(s) ignorée(s)";
        if (skipped > 0)
            message += " (matricule manquant)";
        return new ImportResultDto(imported, skipped, message + ".");
    }

    private static void Apply(CorrectedHourDto dto, T_HEURECORRIGER e)
    {
        e.MATRICULE_SAGE = dto.Matricule;
        e.DATE_POINTAGE_IN = dto.DateIn;
        e.DATE_POINTAGE_OUT = dto.DateOut;
        e.HENTREE = dto.HEntree;
        e.HPS = dto.HPs;
        e.HPE = dto.HPe;
        e.HSORTIE = dto.HSortie;
        e.ABSENCE_AM = dto.AbsAm;
        e.ABSENCE_PM = dto.AbsPm;
        e.INTITULE_ABSENCE = dto.IntituleAbsence;
        e.RETARD = dto.Retard;
        e.FERIER = dto.Ferie;
        e.FERIER_AM = dto.FerieAm;
        e.FERIER_PM = dto.FeriePm;
        e.VALIDER_CORRECTION = dto.ValiderCorrection;
        e.HS = dto.Hs;
        e.M_NUIT = dto.MNuit;
        e.M_DIMANCHE = dto.MDimanche;
        e.M_FERIES = dto.MFeries;
    }

    private static CorrectedHourDto MapCorrection(T_HEURECORRIGER a) => new(
        a.ID, a.MATRICULE_SAGE, a.DATE_POINTAGE_IN, a.DATE_POINTAGE_OUT, a.HENTREE, a.HPS, a.HPE, a.HSORTIE,
        a.ABSENCE_AM, a.ABSENCE_PM, a.INTITULE_ABSENCE, a.RETARD, a.FERIER, a.FERIER_AM, a.FERIER_PM,
        a.VALIDER_CORRECTION, a.VALIDER_HS, a.HS, a.M_NUIT, a.M_DIMANCHE, a.M_FERIES);
}
