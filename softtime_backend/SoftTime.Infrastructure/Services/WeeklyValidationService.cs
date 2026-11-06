using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class WeeklyValidationService : IWeeklyValidationService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IOvertimeWeekContext _overtimeWeekContext;

    public WeeklyValidationService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IOvertimeWeekContext overtimeWeekContext)
    {
        _uow = uow;
        _tenant = tenant;
        _overtimeWeekContext = overtimeWeekContext;
    }

    public async Task<IReadOnlyList<WeeklyHsDto>> PreviewWeekAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMondayToSunday(request);
        return await ComputeWeekAsync(request, persistNonValidated: true, ct);
    }

    /// <summary>
    /// Aperçu paginé de la validation semaine. Lecture seule : contrairement à
    /// <see cref="PreviewWeekAsync"/>, ne persiste pas les lignes non validées, afin de ne pas
    /// écrire à chaque changement de page. Les semaines déjà validées sont renvoyées telles quelles
    /// (elles ne sont plus recalculées) pour pouvoir les annuler depuis l'écran.
    /// </summary>
    public async Task<PagedResult<WeeklyHsDto>> PreviewWeekPagedAsync(
        PeriodRequest request, int? page, int? pageSize, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMondayToSunday(request);
        var rows = await ComputeWeekAsync(request, persistNonValidated: false, ct);
        var filtered = rows
            .Where(row => SearchText.Matches(request.Search, row.Matricule))
            .ToList();
        return PagedLists.Page(filtered, page, pageSize);
    }

    /// <summary>
    /// Valide (ou annule la validation de) la semaine pour les salariés sélectionnés.
    /// La validation fige la semaine (<c>VALIDER_HS</c>), l'annulation la rouvre (synthèse §5).
    /// </summary>
    public async Task ValidateWeekAsync(WeeklyValidationRequest request, bool validate, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMondayToSunday(request);

        var selected = new HashSet<string>(
            (request.SelectedMatricules ?? Array.Empty<string>())
                .Select(MatriculeIdentity.Cle)
                .Where(cle => cle.Length > 0),
            StringComparer.Ordinal);
        if (selected.Count == 0)
            return;

        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var hsRepo = _uow.Repository<HsValidee>();
        var corrRepo = _uow.Repository<HeureCorrigee>();

        // Un recalcul est nécessaire pour obtenir les valeurs à valider (les semaines déjà validées
        // sont exclues du calcul et donc absentes de `rows`).
        if (validate)
            await ComputeWeekAsync(request, persistNonValidated: true, ct);
        else
            await RefuserSiSemaineCalculeeAsync(sageDb, from, selected, ct);

        var lignes = (await hsRepo.ListAsync(x => x.BddSage == sageDb && x.DateDeb == from, ct))
            .Where(x => !string.IsNullOrEmpty(x.Matricules) && selected.Contains(MatriculeIdentity.Cle(x.Matricules)))
            .GroupBy(x => MatriculeIdentity.Cle(x.Matricules))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).First(), StringComparer.Ordinal);

        var corrections = await corrRepo.ListAsync(h => h.BddSage == sageDb
            && h.DatePointageIn >= from && h.DatePointageIn <= to, ct);
        var correctionsParCle = corrections
            .Where(h => !string.IsNullOrEmpty(h.MatriculeSage))
            .GroupBy(h => MatriculeIdentity.Cle(h.MatriculeSage))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var (cle, ligne) in lignes)
        {
            if (validate && ligne.ValidationHs == true)
                continue;
            if (!validate && ligne.ValidationHs != true)
                continue;

            ligne.ValidationHs = validate;
            ligne.DateFin = to;
            hsRepo.Update(ligne);

            if (correctionsParCle.TryGetValue(cle, out var heures))
            {
                foreach (var heure in heures)
                {
                    heure.ValiderHs = validate;
                    corrRepo.Update(heure);
                }
            }
        }

        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Purge la validation d'une semaine (lundi–dimanche) : supprime les lignes <c>T_HS_VALIDER</c>
    /// et déverrouille les heures corrigées correspondantes. Refuse si le calcul EXO/IMPO existe
    /// encore, pour ne pas laisser des montants payés sans validation.
    /// </summary>
    public async Task<PurgeValidationResultDto> PurgeValidationAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsureMondayToSunday(request);
        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;

        var repo = _uow.Repository<HsValidee>();
        var lignes = (await repo.ListAsync(
                v => v.BddSage == sageDb && v.DateDeb == from && v.DateFin == to, ct))
            .Where(v => MatriculeRange.Contains(v.Matricules, request))
            .ToList();
        if (lignes.Count == 0)
            return new PurgeValidationResultDto(0, 0, "Aucune validation à purger sur la période.");

        await RefuserSiSemaineCalculeeAsync(
            sageDb,
            from,
            lignes.Select(v => MatriculeIdentity.Cle(v.Matricules)).Where(cle => cle.Length > 0).ToHashSet(StringComparer.Ordinal),
            ct);

        var corrRepo = _uow.Repository<HeureCorrigee>();
        var corrections = await corrRepo.ListAsync(h => h.BddSage == sageDb
            && h.DatePointageIn >= from && h.DatePointageIn <= to, ct);
        var correctionsParCle = corrections
            .Where(h => !string.IsNullOrEmpty(h.MatriculeSage))
            .GroupBy(h => MatriculeIdentity.Cle(h.MatriculeSage))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var deverrouillees = 0;
        foreach (var ligne in lignes)
        {
            if (!correctionsParCle.TryGetValue(MatriculeIdentity.Cle(ligne.Matricules), out var heures))
                continue;
            foreach (var heure in heures)
            {
                if (heure.ValiderHs != true)
                    continue;
                heure.ValiderHs = false;
                corrRepo.Update(heure);
                deverrouillees++;
            }
        }

        repo.RemoveRange(lignes);
        await _uow.SaveChangesAsync(ct);
        return new PurgeValidationResultDto(lignes.Count, deverrouillees,
            $"{lignes.Count} validation(s) purgée(s), {deverrouillees} heure(s) corrigée(s) déverrouillée(s).");
    }

    /// <summary>
    /// Calcul hebdomadaire (étape 1 de la chaîne). Les salariés dont la semaine est déjà validée
    /// (<c>VALIDATION_HS = true</c>) sont renvoyés tels quels : leur ligne est la source de vérité
    /// et n'est plus recalculée (elle doit rester visible pour pouvoir annuler la validation).
    /// </summary>
    private async Task<List<WeeklyHsDto>> ComputeWeekAsync(PeriodRequest request, bool persistNonValidated, CancellationToken ct)
    {
        var from = request.From.Date;
        var to = request.To.Date;
        var ctx = await _overtimeWeekContext.ChargerContexteAsync(request, avecExistantes: true, ct);
        var hsRepo = _uow.Repository<HsValidee>();
        var resultat = new List<WeeklyHsDto>(ctx.Cards.Count);

        foreach (var card in ctx.Cards)
        {
            var cle = MatriculeIdentity.Cle(card.SageMatricule);
            ctx.Existantes.TryGetValue(cle, out var existante);
            if (existante?.ValidationHs == true)
            {
                resultat.Add(DepuisEntite(existante));
                continue;
            }

            var config = _overtimeWeekContext.Configurer(ctx, card);
            var semaine = OvertimeCalculator.CalculerSemaine(config, _overtimeWeekContext.ConstruireJours(ctx, card, from, to));
            if (persistNonValidated)
                existante = await EnregistrerSemaineAsync(hsRepo, ctx, card, existante, semaine, from, to, ct);

            resultat.Add(new WeeklyHsDto(
                existante?.Id ?? 0, card.SageMatricule, from, to,
                semaine.Ht, semaine.Hs, semaine.Nuit, semaine.Dimanche, semaine.Ferie,
                semaine.Retard, semaine.Absence, Validated: false));
        }

        if (persistNonValidated)
            await _uow.SaveChangesAsync(ct);
        return resultat;
    }

    private static WeeklyHsDto DepuisEntite(HsValidee ligne)
        => new(ligne.Id, ligne.Matricules, ligne.DateDeb, ligne.DateFin,
            ligne.Ht, ligne.Hs, ligne.Nuit, ligne.Dimanche, ligne.Feries,
            ligne.Retard, ligne.Absence, ligne.ValidationHs == true);

    private static async Task<HsValidee> EnregistrerSemaineAsync(
        IRepository<HsValidee> repo,
        WeeklyContext ctx,
        CardPaie card,
        HsValidee? existante,
        OvertimeWeekResult semaine,
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        if (existante != null)
        {
            existante.BddPointage = ctx.PointeuseDb;
            existante.DateFin = to;
            existante.Ht = semaine.Ht;
            existante.Hs = semaine.Hs;
            existante.Nuit = semaine.Nuit;
            existante.Dimanche = semaine.Dimanche;
            existante.Feries = semaine.Ferie;
            existante.Retard = semaine.Retard;
            existante.Absence = semaine.Absence;
            existante.ValidationHs = false;
            repo.Update(existante);
            return existante;
        }

        var nouvelle = new HsValidee
        {
            BddSage = ctx.SageDb,
            BddPointage = ctx.PointeuseDb,
            Matricules = card.SageMatricule,
            DateDeb = from,
            DateFin = to,
            Ht = semaine.Ht,
            Hs = semaine.Hs,
            Nuit = semaine.Nuit,
            Dimanche = semaine.Dimanche,
            Feries = semaine.Ferie,
            Retard = semaine.Retard,
            Absence = semaine.Absence,
            ValidationHs = false
        };
        await repo.AddAsync(nouvelle, ct);
        return nouvelle;
    }

    /// <summary>
    /// Refuse de rouvrir (annuler ou purger) une semaine déjà ventilée dans un calcul EXO/IMPO : ce
    /// calcul a pu être poussé en paie, il faut d'abord purger les heures supplémentaires de la
    /// période qui contient la semaine (même rattachement que <see cref="CalculateExoImpoAsync"/>).
    /// </summary>
    private async Task RefuserSiSemaineCalculeeAsync(
        string sageDb, DateTime lundi, IReadOnlySet<string> cles, CancellationToken ct)
    {
        if (cles.Count == 0)
            return;
        var dimanche = lundi.Date.AddDays(6);
        var calcules = (await _uow.Repository<HsExoImp>().ListAsync(
                h => h.BddSage == sageDb && h.PeriodeDebut <= dimanche && h.PeriodeFin >= dimanche, ct))
            .Where(h => h.Matricule != null && cles.Contains(MatriculeIdentity.Cle(h.Matricule)))
            .OrderBy(h => h.Matricule, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (calcules.Count == 0)
            return;

        var periode = calcules[0];
        var matricules = string.Join(", ", calcules.Select(h => h.Matricule).Distinct().Take(5))
                         + (calcules.Count > 5 ? "…" : string.Empty);
        throw new DomainException(
            $"La semaine du {lundi:dd/MM/yyyy} est incluse dans le calcul EXO/IMPO du "
            + $"{periode.PeriodeDebut:dd/MM/yyyy} au {periode.PeriodeFin:dd/MM/yyyy} ({matricules}) : "
            + "purgez d'abord les heures supplémentaires de cette période.");
    }

    private static void EnsureMondayToSunday(PeriodRequest request)
    {
        if (!OvertimeRules.IsMondayToSundayWeek(request.From, request.To))
            throw new DomainException("La période doit être un lundi–dimanche (7 jours).");
    }
}
