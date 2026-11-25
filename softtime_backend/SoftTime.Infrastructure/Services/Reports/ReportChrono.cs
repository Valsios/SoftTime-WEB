using System.Diagnostics;

using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions.Reports;
using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Services.Reports;

/// <summary>
/// Chronomètre d'une requête d'état calculé (absences, absences justifiées, retards) : durée du chargement (avec le détail
/// fourni par le contexte), du calcul, de l'écriture d'impression et de la pagination, volumes lus et produits. Une seule
/// ligne de journal par requête. Aucune donnée personnelle : ni matricule, ni nom, ni valeur de filtre (seulement les filtres
/// renseignés, désignés par leur nom).
/// </summary>
internal sealed class ReportChrono
{
    private readonly ILogger? _logger;
    private readonly string _type;
    private readonly ReportFilter _filter;
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private long _chargement;
    private long _calcul;
    private long _impression;
    private long _pagination;
    private ReportLoadStats? _stats;

    public ReportChrono(ILogger? logger, string type, ReportFilter filter)
    {
        _logger = logger;
        _type = type;
        _filter = filter;
    }

    /// <summary>Charge le contexte en mesurant la durée ; les détails du chargement sont repris du contexte.</summary>
    public async Task<ReportContext?> ChargerAsync(Func<Task<ReportContext?>> charger)
    {
        var debut = Stopwatch.GetTimestamp();
        var ctx = await charger();
        _chargement = (long)Stopwatch.GetElapsedTime(debut).TotalMilliseconds;
        _stats = ctx?.Mesures;
        return ctx;
    }

    public T Calculer<T>(Func<T> calculer)
    {
        var debut = Stopwatch.GetTimestamp();
        var resultat = calculer();
        _calcul = (long)Stopwatch.GetElapsedTime(debut).TotalMilliseconds;
        return resultat;
    }

    public async Task EcrireAsync(Func<Task> ecrire)
    {
        var debut = Stopwatch.GetTimestamp();
        await ecrire();
        _impression = (long)Stopwatch.GetElapsedTime(debut).TotalMilliseconds;
    }

    public T Paginer<T>(Func<T> paginer)
    {
        var debut = Stopwatch.GetTimestamp();
        var resultat = paginer();
        _pagination = (long)Stopwatch.GetElapsedTime(debut).TotalMilliseconds;
        return resultat;
    }

    /// <summary>Écrit la ligne de journal (niveau Information) : <paramref name="mode"/> = « liste » ou « pagine ».</summary>
    public void Journaliser(string mode, int produites)
    {
        if (_logger is null || !_logger.IsEnabled(LogLevel.Information))
            return;

        var s = _stats;
        _logger.LogInformation(
            "[Rapports] type={Type} mode={Mode} periode={Du:yyyy-MM-dd}..{Au:yyyy-MM-dd} filtres={Filtres} "
            + "chargement={Chargement} ms (cartes={Cartes} ms/{CartesLues}, feries={Feries} ms, conges={Conges} ms, "
            + "corrections={Corrections} ms/{CorrectionsLues}, plannings={Plannings} ms/{PlanningsLues}, autres={Autres} ms, "
            + "filtres_sql={FiltresSql}) calcul={Calcul} ms impression={Impression} ms pagination={Pagination} ms "
            + "total={Total} ms lues={Lues} produites={Produites}",
            _type, mode, _filter.From, _filter.To, NomsDesFiltres(_filter),
            _chargement, s?.CartesMs ?? 0, s?.CartesLues ?? 0, s?.FeriesMs ?? 0, s?.CongesMs ?? 0,
            s?.CorrectionsMs ?? 0, s?.CorrectionsLues ?? 0, s?.PlanningsMs ?? 0, s?.PlanningsLues ?? 0, s?.AutresMs ?? 0,
            s?.FiltresPousses ?? false, _calcul, _impression, _pagination,
            _total.ElapsedMilliseconds, s?.LignesLues ?? 0, produites);
    }

    private static string NomsDesFiltres(ReportFilter filter)
    {
        var noms = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(filter.MatriculeFrom) || !string.IsNullOrWhiteSpace(filter.MatriculeTo))
            noms.Add("matricules");
        if (!string.IsNullOrWhiteSpace(filter.Departement))
            noms.Add("departement");
        return noms.Count == 0 ? "aucun" : string.Join('+', noms);
    }
}
