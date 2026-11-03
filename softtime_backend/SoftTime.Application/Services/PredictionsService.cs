using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain;

namespace SoftTime.Application.Services;

/// <summary>
/// Prévisions : lecture réservée à l'administrateur ou à la porte 6 avec la permission <c>REPORTS:PREDICTIONS</c>
/// effective ; traitements réservés à l'administrateur, un seul à la fois.
/// </summary>
public class PredictionsService : IPredictionsService
{
    public const string ReadPermission = "REPORTS:PREDICTIONS";
    public const string JobAlreadyRunningMessage = "Un traitement ML est déjà en cours.";
    private const int DefaultAbsenceDays = 14;

    private readonly IMlPredictionService _predictions;
    private readonly IMlJobCoordinator _jobs;
    private readonly ICurrentUser _user;
    private readonly ICompanyContext _company;
    private readonly IEffectiveRightsService _rights;

    public PredictionsService(IMlPredictionService predictions, IMlJobCoordinator jobs, ICurrentUser user, ICompanyContext company, IEffectiveRightsService rights)
    {
        _predictions = predictions;
        _jobs = jobs;
        _user = user;
        _company = company;
        _rights = rights;
    }

    public async Task<MlStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        await EnsureCanReadAsync(ct);
        return await _predictions.GetStatusAsync(ct);
    }

    public async Task<MlJobState> GetJobAsync(CancellationToken ct = default)
    {
        await EnsureCanReadAsync(ct);
        return _jobs.State;
    }

    public async Task<IReadOnlyList<MlAbsenceForecastDto>> GetAbsenceAsync(
        DateTime? from, DateTime? to, string? departement, int? categorie, CancellationToken ct = default)
    {
        await EnsureCanReadAsync(ct);
        var debut = (from ?? DateTime.Today).Date;
        var fin = (to ?? debut.AddDays(DefaultAbsenceDays)).Date;
        return await _predictions.GetAbsenceForecastsAsync(debut, fin < debut ? debut : fin, departement, categorie, ct);
    }

    public async Task<IReadOnlyList<MlHsForecastDto>> GetOvertimeAsync(DateTime? week, CancellationToken ct = default)
    {
        await EnsureCanReadAsync(ct);
        return await _predictions.GetHsForecastsAsync((week ?? DateTime.Today).Date, ct);
    }

    public string Schedule(PredictionJob job)
    {
        AdminAccess.Require(_user);
        var (request, message) = job switch
        {
            PredictionJob.Refresh => (new MlJobRequest(_company.SageDatabase, FullRefresh: false, Train: false),
                "Rafraîchissement des données et des prévisions programmé."),
            PredictionJob.Backfill => (new MlJobRequest(_company.SageDatabase, FullRefresh: true, Train: true),
                "Reconstruction complète de la base analytique programmée."),
            _ => (new MlJobRequest(_company.SageDatabase, FullRefresh: false, Train: true), "Réentraînement programmé.")
        };
        return _jobs.TryEnqueue(request) ? message : throw new ConflictException(JobAlreadyRunningMessage);
    }

    private Task EnsureCanReadAsync(CancellationToken ct)
        => FineReadAccess.EnsureAsync(_user, _rights, Rights.Reports, ReadPermission, ct);
}
