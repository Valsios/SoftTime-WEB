using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.CardSync;

/// <summary>Données lues dans SAGE et la pointeuse pour une synchronisation des cartes de paie.</summary>
internal sealed class CardPaieSyncSource
{
    private readonly IReadOnlyList<ExternalAffectation> _affectations;
    private readonly Dictionary<string, string?> _departements;

    private CardPaieSyncSource(
        IReadOnlyList<ExternalEmployee> employees,
        IReadOnlyList<ExternalPunchUser> badges,
        IReadOnlyList<ExternalAffectation> affectations,
        Dictionary<string, string?> departements)
    {
        Employees = employees;
        Badges = badges;
        _affectations = affectations;
        _departements = departements;
    }

    public IReadOnlyList<ExternalEmployee> Employees { get; }

    public IReadOnlyList<ExternalPunchUser> Badges { get; }

    public static async Task<CardPaieSyncSource> LoadAsync(
        IExternalSourceService source, BddSage sageRow, BddPointeuse pteRow, CancellationToken ct)
    {
        var employees = await source.GetEmployeesAsync(sageRow, ct);
        var badges = await source.GetPunchUsersAsync(pteRow, ct);
        var affectations = await source.GetAffectationsAsync(sageRow, ct);
        var departements = (await source.GetDepartmentsAsync(sageRow, ct))
            .GroupBy(d => d.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Label, StringComparer.OrdinalIgnoreCase);
        return new CardPaieSyncSource(employees, badges, affectations, departements);
    }

    /// <summary>Badge pointeuse du salarié : par numéro de sécurité sociale, matricule ou badge SAGE.</summary>
    public ExternalPunchUser? Badge(ExternalEmployee salarie, string matricule) =>
        Badges.FirstOrDefault(b =>
            MatriculeIdentity.MemeIdentifiant(b.Ssn, matricule)
            || MatriculeIdentity.MemeIdentifiant(b.Badge, matricule)
            || MatriculeIdentity.MemeIdentifiant(b.Badge, salarie.Badge));

    /// <summary>
    /// Intitulé du département de l'affectation en cours (sinon la plus récente), ou son code
    /// quand l'intitulé est inconnu.
    /// </summary>
    public string? Departement(long numSalarie)
    {
        var aff = _affectations.Where(a => a.EmployeeId == numSalarie && a.DateSortie == null)
                      .OrderByDescending(a => a.DateDebut).FirstOrDefault()
                  ?? _affectations.Where(a => a.EmployeeId == numSalarie)
                      .OrderByDescending(a => a.DateDebut).FirstOrDefault();
        if (aff?.Departement == null)
            return null;
        return _departements.TryGetValue(aff.Departement, out var intitule) && !string.IsNullOrWhiteSpace(intitule)
            ? intitule
            : aff.Departement;
    }
}
