using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

/// <summary>
/// Présentation de l'analyse de clôture : ajoute nom et prénom aux anomalies de la page courante,
/// depuis <c>T_CARDPAIE</c> de la base active (clé <see cref="MatriculeIdentity.Cle"/>, carte de plus
/// petit ID comme les états), en UNE requête ensembliste. Avec <c>Closing:ShowNames = false</c>,
/// aucune lecture : matricule seul, nom et prénom <c>null</c>. Lecture seule.
/// </summary>
public class ClosingPresentationService : IClosingPresentationService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly ClosingOptions _reglages;

    public ClosingPresentationService(IUnitOfWork uow, ITenantConnectionService tenant, IOptions<ClosingOptions>? reglages = null)
    {
        _uow = uow;
        _tenant = tenant;
        _reglages = reglages?.Value ?? new ClosingOptions();
    }

    public async Task<ClosingReportDto> PresenterAsync(ClosingAnalysisDto analyse, CancellationToken ct = default)
    {
        var afficherNoms = _reglages.ShowNames;
        var noms = new Dictionary<string, (string? Nom, string? Prenom)>(StringComparer.Ordinal);
        if (afficherNoms && analyse.Anomalies.Items.Count > 0)
        {
            var sageDb = _tenant.SageDb;
            var repo = _uow.Repository<CardPaie>();
            var cartes = await repo.ToListAsync(repo.QueryReadOnly()
                .Where(c => c.SageBdd == sageDb)
                .Select(c => new CardPaie { Id = c.Id, SageMatricule = c.SageMatricule, SageNom = c.SageNom, SagePrenom = c.SagePrenom }), ct);
            var clesPage = analyse.Anomalies.Items.Select(a => a.Matricule).ToHashSet(StringComparer.Ordinal);
            foreach (var carte in cartes.OrderBy(c => c.Id))
            {
                var cle = MatriculeIdentity.Cle(carte.SageMatricule);
                if (clesPage.Contains(cle) && !noms.ContainsKey(cle))
                    noms[cle] = (Texte(carte.SageNom), Texte(carte.SagePrenom));
            }
        }

        var items = analyse.Anomalies.Items
            .Select(a =>
            {
                var (nom, prenom) = noms.GetValueOrDefault(a.Matricule);
                return new ClosingAnomalyViewDto(a.Type, a.Gravite, a.Poids, a.Matricule, nom, prenom, a.Departement, a.Date, a.Detail, a.Heures, a.PointageExiste);
            })
            .ToList();
        var page = analyse.Anomalies;
        var statut = Enum.TryParse<ClosingStatus>(analyse.Statut, out var s) ? s : (ClosingStatus?)null;

        return new ClosingReportDto(
            analyse.From,
            analyse.To,
            analyse.Departement,
            analyse.Statut,
            statut is ClosingStatus valeur ? ClosingStatusRules.Libelle(valeur) : null,
            _reglages.CriticalRatioBlocking,
            analyse.Score,
            analyse.Formule,
            analyse.JoursEmployeControles,
            analyse.NombreAnomalies,
            analyse.ParGravite,
            analyse.ParType,
            new PagedResult<ClosingAnomalyViewDto>(items, page.Total, page.Page, page.PageSize, page.TotalPages),
            analyse.ParDepartement,
            analyse.Comparaison,
            afficherNoms,
            analyse.SalariesSansPlanningExclus);
    }

    private static string? Texte(string? valeur) => string.IsNullOrWhiteSpace(valeur) ? null : valeur.Trim();
}
