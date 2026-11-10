using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions.Ml;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Api.HostedServices;

/// <summary>
/// Scheduler du moteur prédictif : consomme les jobs demandés par l'API et exécute le cycle
/// quotidien (rafraîchissement incrémental + prédictions, entraînement selon la périodicité).
/// Chaque société est traitée dans son propre scope avec son contexte de tenant.
/// </summary>
public sealed class MlMaintenanceHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MlJobCoordinator _jobs;
    private readonly MlOptions _options;
    private readonly ILogger<MlMaintenanceHostedService> _logger;
    private DateTime _dernierQuotidien = DateTime.MinValue;

    public MlMaintenanceHostedService(
        IServiceScopeFactory scopeFactory,
        MlJobCoordinator jobs,
        IOptions<MlOptions> options,
        ILogger<MlMaintenanceHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _jobs = jobs;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Moteur prédictif désactivé (Ml:Enabled=false).");
            return;
        }

        _logger.LogInformation(
            "Scheduler ML démarré (heure quotidienne={Hour}, horizon={Days} j, {Weeks} sem.).",
            _options.RefreshHourLocal, _options.ForecastHorizonDays, _options.ForecastHorizonWeeks);

        // Laisser l'application démarrer et appliquer les migrations avant le premier cycle.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur du scheduler ML.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    /// <summary>Traite les demandes en attente puis, une fois par jour à l'heure prévue, le rafraîchissement.</summary>
    private async Task CycleAsync(CancellationToken ct)
    {
        while (_jobs.Reader.TryRead(out var job))
        {
            _logger.LogInformation(
                "Job ML reçu (société={Tenant}, full={Full}, train={Train}).",
                job.BddSage ?? "toutes", job.FullRefresh, job.Train);
            await ExecuterAsync(job, ct);
        }

        var maintenant = DateTime.Now;
        if (maintenant.Hour == _options.RefreshHourLocal && _dernierQuotidien.Date < maintenant.Date)
        {
            _dernierQuotidien = maintenant;
            await ExecuterAsync(new MlJobRequest(null, FullRefresh: false, Train: false), ct);
        }
    }

    private async Task ExecuterAsync(MlJobRequest job, CancellationToken ct)
    {
        _jobs.SetRunning("Traitement ML en cours…");
        try
        {
            var tenants = (await ChargerTenantsAsync(ct))
                .Where(t => string.IsNullOrWhiteSpace(job.BddSage) || string.Equals(t, job.BddSage, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var erreurs = 0;
            foreach (var tenant in tenants)
            {
                if (!await ExecuterPourAsync(job, tenant, ct))
                    erreurs++;
            }

            if (erreurs == 0)
                _jobs.SetIdle("Traitement ML terminé.");
            else
                _jobs.SetIdle($"Traitement ML terminé avec {erreurs} erreur(s) société.", "Voir les logs du serveur.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du traitement ML.");
            _jobs.SetIdle("Échec du traitement ML.", ex.Message);
        }
    }

    /// <summary>Pipeline ML d'une société, dans un scope système dédié ; faux en cas d'échec (journalisé).</summary>
    private async Task<bool> ExecuterPourAsync(MlJobRequest job, string tenant, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            // Avant toute résolution d'ICurrentUser : le scope n'a pas d'utilisateur HTTP.
            scope.ServiceProvider.GetRequiredService<SystemExecution>().Enable();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(tenant, null);
            var pipeline = scope.ServiceProvider.GetRequiredService<IMlPipelineService>();
            var result = await pipeline.RunAsync(job.FullRefresh, job.Train, ct);
            _logger.LogInformation("ML {Tenant} : {Etat} — {Message}", tenant, result.Etat, result.Message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du traitement ML pour {Tenant}.", tenant);
            return false;
        }
    }

    private async Task<List<string>> ChargerTenantsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var sages = await uow.Repository<BddSage>().ListAsync(_ => true, ct);
        var cartes = await uow.Repository<CardPaie>().ListAsync(_ => true, ct);

        var avecCartes = cartes
            .Where(c => !string.IsNullOrWhiteSpace(c.SageBdd))
            .Select(c => c.SageBdd!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return sages
            .Where(s => !string.IsNullOrWhiteSpace(s.NomBd) && avecCartes.Contains(s.NomBd!))
            .Select(s => s.NomBd!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
