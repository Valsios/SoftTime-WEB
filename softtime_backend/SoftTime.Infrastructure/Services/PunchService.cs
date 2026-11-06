using System.Linq.Expressions;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class PunchService : IPunchService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;
    private readonly IHistoryArchiveService _history;
    private readonly IValidatedWeekGuard _validatedWeekGuard;

    public PunchService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource,
        IHistoryArchiveService history,
        IValidatedWeekGuard validatedWeekGuard)
    {
        _uow = uow;
        _tenant = tenant;
        _externalSource = externalSource;
        _history = history;
        _validatedWeekGuard = validatedWeekGuard;
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
        await _validatedWeekGuard.RefuserSiSemaineValideeAsync(sageDb, entity.MatriculeSage, jour, ct);

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
}
