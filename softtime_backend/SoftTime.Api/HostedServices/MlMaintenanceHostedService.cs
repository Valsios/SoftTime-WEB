using Microsoft.Extensions.Options;
using SoftTime.Application.Abstractions;
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

        var dernierQuotidien = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (_jobs.Reader.TryRead(out var job))
                {
                    _logger.LogInformation(
                        "Job ML reçu (société={Tenant}, full={Full}, train={Train}).",
                        job.BddSage ?? "toutes", job.FullRefresh, job.Train);
                    await ExecuterAsync(job, stoppingToken);
                }

                var maintenant = DateTime.Now;
                if (maintenant.Hour == _options.RefreshHourLocal && dernierQuotidien.Date < maintenant.Date)
                {
                    dernierQuotidien = maintenant;
                    await ExecuterAsync(new MlJobRequest(null, FullRefresh: false, Train: false), stoppingToken);
                }
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

    private async Task ExecuterAsync(MlJobRequest job, CancellationToken ct)
    {
        _jobs.SetRunning("Traitement ML en cours…");
        try
        {
            var tenants = await ChargerTenantsAsync(ct);
            if (!string.IsNullOrWhiteSpace(job.BddSage))
            {
                tenants = tenants
                    .Where(t => string.Equals(t, job.BddSage, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var erreurs = 0;
            foreach (var tenant in tenants)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(tenant, null);
                    var pipeline = scope.ServiceProvider.GetRequiredService<MlPipelineService>();
                    var result = await pipeline.RunAsync(job.FullRefresh, job.Train, ct);
                    _logger.LogInformation(
                        "ML {Tenant} : {Etat} — {Message}", tenant, result.Etat, result.Message);
                }
                catch (Exception ex)
                {
                    erreurs++;
                    _logger.LogError(ex, "Échec du traitement ML pour {Tenant}.", tenant);
                }
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

    private async Task<List<string>> ChargerTenantsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var sages = await uow.Repository<T_BDD_SAGE>().ListAsync(_ => true, ct);
        var cartes = await uow.Repository<T_CARDPAIE>().ListAsync(_ => true, ct);

        var avecCartes = cartes
            .Where(c => !string.IsNullOrWhiteSpace(c.SAGE_BDD))
            .Select(c => c.SAGE_BDD!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return sages
            .Where(s => !string.IsNullOrWhiteSpace(s.NOM_BD) && avecCartes.Contains(s.NOM_BD!))
            .Select(s => s.NOM_BD!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
