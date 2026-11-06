using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class PunchImportService : IPunchImportService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;

    public PunchImportService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource)
    {
        _uow = uow;
        _tenant = tenant;
        _externalSource = externalSource;
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
