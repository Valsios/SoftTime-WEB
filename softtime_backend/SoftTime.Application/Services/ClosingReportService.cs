using Microsoft.Extensions.Logging;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain;

namespace SoftTime.Application.Services;

/// <summary>
/// Clôture de paie assistée, en lecture seule : la base active est contrôlée par le middleware de tenant,
/// puis la lecture fine (administrateur, ou porte 6 ET permission <c>REPORTS:CLOSING</c> effectives).
/// </summary>
public class ClosingReportService : IClosingReportService
{
    public const string Permission = "REPORTS:CLOSING";
    public const string DisabledMessage = "La clôture de paie assistée est désactivée.";

    private readonly IClosingService _closing;
    private readonly IClosingPresentationService _presentation;
    private readonly ClosingOptions _settings;
    private readonly ICurrentUser _user;
    private readonly IEffectiveRightsService _rights;
    private readonly ILogger<ClosingReportService> _logger;

    public ClosingReportService(
        IClosingService closing,
        IClosingPresentationService presentation,
        ClosingOptions settings,
        ICurrentUser user,
        IEffectiveRightsService rights,
        ILogger<ClosingReportService> logger)
    {
        _closing = closing;
        _presentation = presentation;
        _settings = settings;
        _user = user;
        _rights = rights;
        _logger = logger;
    }

    public async Task<ClosingReportDto> AnalyseAsync(ClosingAnalysisQuery query, CancellationToken ct = default)
    {
        await FineReadAccess.EnsureAsync(_user, _rights, Rights.Reports, Permission, ct);
        if (!_settings.Enabled)
            throw new NotFoundException(DisabledMessage);

        var request = new ClosingAnalysisRequest(
            Day(query.From), Day(query.To), query.Departement, query.Page, query.PageSize, Options: null,
            IncludeComparison: query.IncludeComparison == true, IncludeInformation: query.IncludeInformation == true);
        var rapport = await _presentation.PresenterAsync(await _closing.AnalyserAsync(request, ct), ct);

        // Aucun nom ni matricule dans le journal : période, volumes et statut seulement.
        _logger.LogInformation("Analyse de clôture {From:yyyy-MM-dd} → {To:yyyy-MM-dd} : {Nombre} anomalie(s), statut {Statut}.",
            rapport.From, rapport.To, rapport.NombreAnomalies, rapport.Statut ?? "aucun");
        return rapport;
    }

    /// <summary>Date du jour demandé, convertie en UTC si elle est horodatée en local (comme le journal).</summary>
    private static DateTime Day(DateTime? value)
    {
        if (value is not DateTime date)
            return default;
        return (date.Kind == DateTimeKind.Local ? date.ToUniversalTime() : date).Date;
    }
}
