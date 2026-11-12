using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions.Landing;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Landing;

/// <summary>
/// Atterrissage de semaine pour la société active. Rien n'est stocké ni précalculé : chaque lecture
/// repart des heures corrigées et du planning, et suit donc immédiatement une correction ou un
/// changement de planning.
/// </summary>
public sealed class WeekLandingService : IWeekLandingService
{
    private readonly ILandingContextLoader _loader;
    private readonly TimeProvider _time;
    private readonly LandingOptions _options;
    private readonly ILogger<WeekLandingService> _logger;

    public WeekLandingService(
        ILandingContextLoader loader,
        TimeProvider time,
        IOptions<LandingOptions> options,
        ILogger<WeekLandingService> logger)
    {
        _loader = loader;
        _time = time;
        _options = options.Value;
        _logger = logger;
    }

    private int DriftWeeks => Math.Max(1, _options.DriftWeeks);

    public async Task<WeekLandingDto> GetAsync(DateTime semaine, CancellationToken ct = default)
    {
        var aujourdhui = _time.GetLocalNow().Date;
        var lundi = LundiDe(semaine);
        var ctx = await LoadAsync(lundi, lundi, aujourdhui, ct);
        var lignes = Lignes(ctx, lundi, ObserveJusquA(ctx, aujourdhui));

        _logger.LogInformation(
            "Atterrissage de la semaine du {Lundi:yyyy-MM-dd} : {Salaries} salarié(s), pointages jusqu'au {Dernier:yyyy-MM-dd}.",
            lundi, lignes.Count, ctx.DernierPointage);
        return new WeekLandingDto(
            Totaux(lundi, lignes),
            aujourdhui,
            ctx.DernierPointage,
            OvertimeRules.WeeklyHs130Cap,
            OvertimeRules.ExemptCeilingHours,
            lignes);
    }

    public async Task<IReadOnlyList<WeekLandingWeekDto>> GetHorizonAsync(CancellationToken ct = default)
    {
        var aujourdhui = _time.GetLocalNow().Date;
        var premier = LundiDe(aujourdhui);
        var semaines = Math.Max(1, _options.HorizonWeeks);
        var ctx = await LoadAsync(premier, premier.AddDays(7 * (semaines - 1)), aujourdhui, ct);
        var observe = ObserveJusquA(ctx, aujourdhui);

        return Enumerable.Range(0, semaines)
            .Select(i => premier.AddDays(7 * i))
            .Select(lundi => Totaux(lundi, Lignes(ctx, lundi, observe)))
            .ToList();
    }

    /// <summary>
    /// Charge les semaines demandées et, avant elles, de quoi mesurer l'écart habituel au planning :
    /// les semaines qui précèdent la semaine en cours (ou la semaine demandée si elle est passée).
    /// </summary>
    private Task<LandingContext> LoadAsync(DateTime premierLundi, DateTime dernierLundi, DateTime aujourdhui, CancellationToken ct)
    {
        var reference = premierLundi < LundiDe(aujourdhui) ? premierLundi : LundiDe(aujourdhui);
        return _loader.LoadAsync(reference.AddDays(-7 * (DriftWeeks + 1)), dernierLundi.AddDays(6), ct);
    }

    /// <summary>
    /// Premier jour à projeter : aujourd'hui (la journée n'est pas finie), ou le lendemain du dernier
    /// pointage traité quand l'import a du retard — un jour pas encore importé n'est pas une absence.
    /// </summary>
    private static DateTime ObserveJusquA(LandingContext ctx, DateTime aujourdhui)
    {
        if (ctx.DernierPointage is not DateTime dernier)
            return DateTime.MinValue;
        var lendemain = dernier.AddDays(1);
        return lendemain < aujourdhui ? lendemain : aujourdhui;
    }

    /// <summary>Une ligne par salarié concerné par la semaine, les atterrissages les plus élevés d'abord.</summary>
    private List<WeekLandingRowDto> Lignes(LandingContext ctx, DateTime lundi, DateTime observe)
        => ctx.Employees
            .Select(e => Ligne(e, lundi, observe))
            .Where(l => l is not null)
            .Select(l => l!)
            .OrderByDescending(l => l.HsAtterrissage)
            .ThenBy(l => l.Nom, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Atterrissage d'un salarié ; <c>null</c> s'il n'a ni pointage ni planning cette semaine.</summary>
    private WeekLandingRowDto? Ligne(LandingEmployee employee, DateTime lundi, DateTime observe)
    {
        var semaines = employee.Jours
            .GroupBy(j => LundiDe(j.Jour))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<OvertimeDayInput>)g.ToList());
        if (!semaines.TryGetValue(lundi, out var semaine))
            return null;

        // Seules des semaines entièrement pointées disent quelque chose de l'écart au planning.
        var pointees = semaines
            .Where(s => s.Key < lundi && s.Key.AddDays(6) < observe && s.Value.Count == 7)
            .OrderByDescending(s => s.Key)
            .Take(DriftWeeks)
            .Select(s => s.Value);
        var derive = WeekLandingRules.Deriver(employee.Config, pointees);
        var resultat = WeekLandingRules.Calculer(employee.Config, semaine, observe, derive);

        var concerne = resultat.HeuresRealisees > 0m || resultat.HeuresPlanifieesRestantes > 0m || resultat.HsAtterrissage > 0m;
        return concerne ? ToDto(employee, resultat, derive) : null;
    }

    private static WeekLandingRowDto ToDto(LandingEmployee employee, WeekLandingResult resultat, WeekDrift derive)
        => new(
            employee.Card.SageMatricule?.Trim() ?? string.Empty,
            employee.Card.SageNom,
            employee.Card.SagePrenom,
            employee.Card.Departement,
            employee.Config.HeuresSemaine,
            resultat.HeuresRealisees,
            resultat.HsAcquises,
            resultat.HeuresPlanifieesRestantes,
            resultat.JoursSansPlanning,
            resultat.HsSelonPlanning,
            resultat.Derive,
            derive.Semaines,
            resultat.HsAtterrissage,
            (int)resultat.Niveau,
            resultat.Seuil,
            resultat.HeuresARetirer,
            resultat.DejaFranchi);

    private static WeekLandingWeekDto Totaux(DateTime lundi, IReadOnlyList<WeekLandingRowDto> lignes)
        => new(
            lundi,
            lignes.Count,
            lignes.Count(l => l.Niveau >= (int)LandingLevel.HeuresSup),
            lignes.Count(l => l.Niveau >= (int)LandingLevel.Majoration150),
            lignes.Count(l => l.Niveau >= (int)LandingLevel.Plafond),
            lignes.Count(l => l.JoursSansPlanning > 0),
            lignes.Sum(l => l.HsAcquises),
            lignes.Sum(l => l.HsAtterrissage),
            lignes.Sum(l => l.HeuresARetirer));

    private static DateTime LundiDe(DateTime date)
        => date.Date.AddDays(-(((int)date.Date.DayOfWeek + 6) % 7));
}
