using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain;
using SoftTime.Domain.Models;

namespace SoftTime.Api.Controllers;

/// <summary>
/// Clôture de paie assistée, en LECTURE SEULE : aucune route d'écriture (POST, PUT, PATCH, DELETE
/// répondent 405). Contrôles dans le même ordre que <see cref="PredictionsController"/> : la base
/// active (TenantMiddleware puis <see cref="TenantConnectionService.EnsureAuthorizedAsync"/>), puis
/// la lecture fine : administrateur, ou porte 6 ET permission <c>REPORTS:CLOSING</c> effectives.
/// </summary>
[ApiController]
[Authorize]
[Route("api/closing")]
[Tags("Clôture")]
public class ClosingController : ControllerBase
{
    public const string Permission = "REPORTS:CLOSING";
    public const string Desactivee = "La clôture de paie assistée est désactivée.";

    private readonly ClosingService _closing;
    private readonly ClosingPresentationService _presentation;
    private readonly ClosingOptions _reglages;
    private readonly ICurrentUser _user;
    private readonly EffectiveRightsService _rights;
    private readonly ILogger<ClosingController> _logger;

    public ClosingController(
        ClosingService closing,
        ClosingPresentationService presentation,
        ClosingOptions reglages,
        ICurrentUser user,
        EffectiveRightsService rights,
        ILogger<ClosingController> logger)
    {
        _closing = closing;
        _presentation = presentation;
        _reglages = reglages;
        _user = user;
        _rights = rights;
        _logger = logger;
    }

    /// <summary>
    /// Analyse de clôture d'une période (mois civil précédent sans dates) : statut, score, formule,
    /// comptages, départements, page d'anomalies (50 par défaut, 200 au plus) et comparaison si demandée.
    /// La liste omet les anomalies « Information » sauf <c>includeInformation=true</c> ; les comptages les incluent toujours.
    /// <c>reuseCache=true</c> (changement de page seulement) réutilise l'analyse déjà calculée pour la même période (délai glissant de 120 s sous un plafond de 600 s, par défaut).
    /// <c>gravites=Critique,Importante</c> restreint la liste à ces niveaux (Information, Mineure, Importante, Critique) ;
    /// <c>tri=gravite</c> range la liste du plus grave au moins grave. Les comptages, le score et le statut ne dépendent jamais de ces deux paramètres.
    /// </summary>
    [HttpGet("analysis")]
    public async Task<IActionResult> Analysis(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? departement,
        [FromQuery] bool? includeComparison,
        [FromQuery] bool? includeInformation,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool? reuseCache,
        [FromQuery] string? gravites,
        [FromQuery] string? tri,
        CancellationToken ct)
    {
        await FineReadAccess.EnsureAsync(_user, _rights, Rights.Reports, Permission, ct);
        if (!_reglages.Enabled)
            return NotFound(new { error = Desactivee });

        if (!TryLireGravites(gravites, out var niveaux))
            return BadRequest(new { error = "Le paramètre gravites doit lister des niveaux parmi Information, Mineure, Importante, Critique." });
        if (!string.IsNullOrWhiteSpace(tri) && !string.Equals(tri.Trim(), "gravite", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Le paramètre tri n'accepte que la valeur « gravite »." });

        var request = new ClosingAnalysisRequest(
            Jour(from), Jour(to), departement, page, pageSize, Options: null, IncludeComparison: includeComparison == true,
            IncludeInformation: includeInformation == true, ReuseCached: reuseCache == true,
            Gravites: niveaux, TriParGravite: !string.IsNullOrWhiteSpace(tri),
            Perimetre: PerimetreUtilisateur());
        var analyse = await _closing.AnalyserAsync(request, ct);
        var rapport = await _presentation.PresenterAsync(analyse, ct);

        // Aucun nom ni matricule dans le journal : période, volumes et statut seulement.
        _logger.LogInformation("Analyse de clôture {From:yyyy-MM-dd} → {To:yyyy-MM-dd} : {Nombre} anomalie(s), statut {Statut}.",
            rapport.From, rapport.To, rapport.NombreAnomalies, rapport.Statut ?? "aucun");
        return Ok(rapport);
    }

    /// <summary>
    /// Checklist d'avancement de la paie (pointages importés, anomalies, semaines validées, calcul EXO/IMPO, heures
    /// imposables, synchronisation paie) pour la même période et le même département que l'analyse. Séparée de l'analyse :
    /// n'entre ni dans le score ni dans le statut. Mêmes contrôles d'accès que l'analyse ; cache de 60 s par utilisateur.
    /// </summary>
    [HttpGet("checklist")]
    public async Task<IActionResult> Checklist(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? departement,
        [FromQuery] bool? reuseCache,
        CancellationToken ct)
    {
        await FineReadAccess.EnsureAsync(_user, _rights, Rights.Reports, Permission, ct);
        if (!_reglages.Enabled)
            return NotFound(new { error = Desactivee });

        var perimetre = PerimetreUtilisateur();
        var checklist = await _closing.ChecklistAsync(
            new ClosingChecklistRequest(Jour(from), Jour(to), departement, reuseCache == true), perimetre, ct);
        _logger.LogInformation("Checklist de clôture {From:yyyy-MM-dd} → {To:yyyy-MM-dd} : {Lignes} ligne(s).", checklist.From, checklist.To, checklist.Lignes.Count);
        return Ok(checklist);
    }

    /// <summary>Périmètre de l'appelant pour la clé des caches : deux utilisateurs ne partagent jamais une entrée.</summary>
    private string PerimetreUtilisateur() => "u" + (_user.UserId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?");

    /// <summary>Niveaux séparés par des virgules (insensible à la casse) ; absent ou vide : aucun filtre ; valeur inconnue : refus.</summary>
    private static bool TryLireGravites(string? valeur, out IReadOnlyList<ClosingSeverity>? niveaux)
    {
        niveaux = null;
        if (string.IsNullOrWhiteSpace(valeur))
            return true;
        var liste = new List<ClosingSeverity>();
        foreach (var morceau in valeur.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<ClosingSeverity>(morceau, ignoreCase: true, out var niveau) || !Enum.IsDefined(niveau))
                return false;
            if (!liste.Contains(niveau))
                liste.Add(niveau);
        }
        niveaux = liste.Count > 0 ? liste : null;
        return true;
    }

    /// <summary>Date du jour demandé, convertie en UTC si elle est horodatée en local (comme le journal).</summary>
    private static DateTime Jour(DateTime? valeur)
    {
        if (valeur is not DateTime date)
            return default;
        return (date.Kind == DateTimeKind.Local ? date.ToUniversalTime() : date).Date;
    }
}
