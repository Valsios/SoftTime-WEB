using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions.Ml;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models.Ml;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Ml;

public sealed class MlForecastQueryService : IMlForecastQueryService
{
    private readonly IMlStore _store;
    private readonly IMlPredictor _predictor;
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly MlOptions _options;

    public MlForecastQueryService(
        IMlStore store,
        IMlPredictor predictor,
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IOptions<MlOptions> options)
    {
        _store = store;
        _predictor = predictor;
        _uow = uow;
        _tenant = tenant;
        _options = options.Value;
    }

    // ------------------------------------------------------------------ lecture

    public async Task<MlStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        // Les modèles d'absence hérités restent en base mais ne font plus partie du moteur.
        var models = (await _store.ListModelsAsync(sage, ct))
            .Where(m => m.TypeModele != MlModelTypes.Absence)
            .ToList();
        var active = models.Where(m => m.EstActif && m.SchemaHash == _predictor.FeatureSchemaHash).ToList();
        var obsoletes = models.Any(m => m.EstActif && m.SchemaHash != _predictor.FeatureSchemaHash);

        var etat = !_options.Enabled
            ? "Desactive"
            : active.Count > 0 ? "Pret" : "Baseline";
        var message = etat switch
        {
            "Desactive" => "Moteur prédictif désactivé par la configuration.",
            "Baseline" when obsoletes => "Mode baseline : les modèles actifs utilisent un ancien contrat de features, un réentraînement est nécessaire.",
            "Baseline" => BaselineMessage(models),
            _ => $"{active.Count} modèle(s) actif(s)."
        };

        var derniereGeneration = await DerniereGenerationAsync(sage, ct);

        return new MlStatusDto(
            _options.Enabled,
            etat,
            message,
            derniereGeneration,
            OvertimeRules.ExemptCeilingHours,
            models.Select(ToDto).ToList());
    }

    /// <summary>Date de la dernière génération des prévisions de l'horizon, si elles existent.</summary>
    private async Task<DateTime?> DerniereGenerationAsync(string sage, CancellationToken ct)
    {
        DateTime? derniereGeneration = null;
        var weekStart = MlWeeks.MondayOf(DateTime.Today);
        var predictions = await _store.GetPredictionsAsync(
            sage, MlModelType.HsMontant, weekStart, weekStart.AddDays(7 * Math.Max(1, _options.ForecastHorizonWeeks) - 1), ct);
        if (predictions.Count > 0)
            derniereGeneration = predictions.Max(p => p.GenereLe);
        return derniereGeneration;
    }

    public async Task<IReadOnlyList<MlHsForecastDto>> GetHsForecastsAsync(
        DateTime semaine, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var debut = MlWeeks.MondayOf(semaine);
        var fin = debut.AddDays(6);

        var probabilites = (await _store.GetPredictionsAsync(sage, MlModelType.HsProbabilite, debut, fin, ct))
            .Where(p => p.Cible == debut)
            .ToDictionary(p => p.Matricule, StringComparer.Ordinal);
        var montants = (await _store.GetPredictionsAsync(sage, MlModelType.HsMontant, debut, fin, ct))
            .Where(p => p.Cible == debut)
            .ToDictionary(p => p.Matricule, StringComparer.Ordinal);

        if (montants.Count == 0)
            return Array.Empty<MlHsForecastDto>();

        var cards = await LoadCardsAsync(sage, ct);
        var resultat = Previsions(debut, probabilites, montants, cards);

        return resultat.OrderByDescending(r => r.HeuresPrevues).ToList();
    }

    /// <summary>Une ligne par salarié prévu : montant, intervalle et probabilité HS, avec l'identité de la carte.</summary>
    private static List<MlHsForecastDto> Previsions(
        DateTime debut, Dictionary<string, MlPrediction> probabilites, Dictionary<string, MlPrediction> montants, Dictionary<string, CardPaie> cards)
    {
        var resultat = new List<MlHsForecastDto>();

        foreach (var (matricule, montant) in montants)
        {
            cards.TryGetValue(matricule, out var card);
            probabilites.TryGetValue(matricule, out var probabilite);
            resultat.Add(new MlHsForecastDto(
                matricule,
                card?.SageNom,
                card?.SagePrenom,
                card?.Departement,
                debut,
                montant.ValeurPrevue ?? 0m,
                montant.BorneBasse ?? 0m,
                montant.BorneHaute ?? 0m,
                probabilite?.Probabilite ?? 0d,
                probabilite?.NiveauRisque ?? 1));
        }
        return resultat;
    }

    /// <summary>
    /// Totaux HS par semaine sur l'horizon (semaine en cours comprise). Les semaines sans
    /// prévision (planning pas encore saisi) sont renvoyées à zéro pour que l'écran l'explique.
    /// </summary>
    public async Task<IReadOnlyList<MlHsWeekDto>> GetHsHorizonAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var debut = MlWeeks.MondayOf(DateTime.Today);
        var semaines = Math.Max(1, _options.ForecastHorizonWeeks);
        var fin = debut.AddDays(7 * semaines - 1);

        var montants = await _store.GetPredictionsAsync(sage, MlModelType.HsMontant, debut, fin, ct);

        return Enumerable.Range(0, semaines)
            .Select(i =>
            {
                var lundi = debut.AddDays(7 * i);
                var lignes = montants.Where(p => p.Cible.Date == lundi).ToList();
                return new MlHsWeekDto(
                    lundi,
                    lignes.Count,
                    lignes.Count(p => (p.ValeurPrevue ?? 0m) > OvertimeRules.ExemptCeilingHours),
                    lignes.Sum(p => p.ValeurPrevue ?? 0m),
                    lignes.Sum(p => p.BorneBasse ?? 0m),
                    lignes.Sum(p => p.BorneHaute ?? 0m));
            })
            .ToList();
    }

    /// <summary>
    /// Prévu / réel des dernières semaines terminées : la dernière prévision émise pour chaque
    /// salarié est comparée aux HS de la base analytique (validées, sinon calculées). Seuls les
    /// salariés présents des deux côtés sont comparés.
    /// </summary>
    public async Task<IReadOnlyList<MlHsAccuracyDto>> GetHsAccuracyAsync(int semaines, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var finDerniere = MlWeeks.MondayOf(DateTime.Today).AddDays(-1);
        var debut = MlWeeks.MondayOf(DateTime.Today).AddDays(-7 * Math.Clamp(semaines, 1, 26));

        var montants = await _store.GetPredictionsAsync(sage, MlModelType.HsMontant, debut, finDerniere, ct);
        if (montants.Count == 0)
            return Array.Empty<MlHsAccuracyDto>();

        var reels = (await _store.GetWeeklyAsync(sage, debut, finDerniere, ct))
            .ToDictionary(w => (w.Matricule, w.SemaineDebut.Date), w => w.Hs);

        var resultat = new List<MlHsAccuracyDto>();
        foreach (var semaine in montants.GroupBy(p => p.Cible.Date).OrderByDescending(g => g.Key))
        {
            var paires = semaine
                .Where(p => reels.ContainsKey((p.Matricule, semaine.Key)))
                .Select(p => (Prevision: p, Reel: reels[(p.Matricule, semaine.Key)]))
                .ToList();
            if (paires.Count == 0)
                continue;

            AjouterPrecision(resultat, semaine, paires);
        }

        return resultat;
    }

    /// <summary>Écart prévu / réel d'une semaine : totaux, écart moyen et part des salariés dans la fourchette.</summary>
    private static void AjouterPrecision(
        List<MlHsAccuracyDto> resultat, IGrouping<DateTime, MlPrediction> semaine, List<(MlPrediction Prevision, decimal Reel)> paires)
    {
        var prevu = paires.Sum(x => x.Prevision.ValeurPrevue ?? 0m);
        var reel = paires.Sum(x => x.Reel);
        var ecartMoyen = paires.Average(x => Math.Abs((x.Prevision.ValeurPrevue ?? 0m) - x.Reel));
        var dansFourchette = paires.Count(x =>
            x.Reel >= (x.Prevision.BorneBasse ?? 0m) && x.Reel <= (x.Prevision.BorneHaute ?? 0m));

        resultat.Add(new MlHsAccuracyDto(
            semaine.Key,
            paires.Count,
            Math.Round(prevu, 2),
            Math.Round(reel, 2),
            Math.Round(ecartMoyen, 2),
            Math.Round((double)dansFourchette / paires.Count, 4)));
    }

    private async Task<Dictionary<string, CardPaie>> LoadCardsAsync(string sage, CancellationToken ct)
    {
        var cards = await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == sage, ct);
        return cards
            .Where(c => !string.IsNullOrWhiteSpace(c.SageMatricule))
            .GroupBy(c => MatriculeIdentity.Cle(c.SageMatricule))
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id).First(), StringComparer.Ordinal);
    }

    private static string BaselineMessage(IReadOnlyList<MlModele> models)
    {
        var dernier = models
            .Where(m => m.Statut == MlModelStatus.Rejete && !string.IsNullOrWhiteSpace(m.Commentaire))
            .OrderByDescending(m => m.EntraineLe)
            .FirstOrDefault();

        return dernier?.Commentaire is { Length: > 0 } raison
            ? $"Mode baseline : {raison}"
            : "Aucun modèle actif : données insuffisantes ou modèle indisponible.";
    }

    private static MlModelDto ToDto(MlModele model) => new(
        model.Id,
        model.TypeModele,
        model.Version,
        model.Algorithme,
        model.Statut,
        model.EstActif,
        model.EntraineLe,
        model.PeriodeDebut,
        model.PeriodeFin,
        model.NbLignes,
        model.NbPositifs,
        model.MetriquesJson,
        model.Commentaire);
}
