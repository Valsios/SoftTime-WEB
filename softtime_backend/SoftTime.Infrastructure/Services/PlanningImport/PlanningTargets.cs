using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.PlanningImport;

/// <summary>Résout les salariés visés par une ligne d'import : par catégorie ou par matricule.</summary>
internal sealed class PlanningTargets
{
    private readonly List<CardPaie> _cards;
    private readonly List<Categorie> _categories;
    private readonly List<CategorieSalarie> _affectations;

    private PlanningTargets(List<CardPaie> cards, List<Categorie> categories, List<CategorieSalarie> affectations)
    {
        _cards = cards;
        _categories = categories;
        _affectations = affectations;
    }

    /// <summary>Charge catégories et affectations du tenant pour les cartes déjà filtrées.</summary>
    public static async Task<PlanningTargets> ChargerAsync(
        IUnitOfWork uow, string sageDb, IPlanningDatabaseFilter filter, List<CardPaie> cards, CancellationToken ct)
    {
        var categories = (await uow.Repository<Categorie>().ListAsync(c => c.BddSage == sageDb, ct))
            .Where(c => filter.MatchesPointeuse(c.BddPointeuse))
            .ToList();
        var affectations = (await uow.Repository<CategorieSalarie>().ListAsync(a => a.BddSage == sageDb, ct))
            .Where(a => filter.MatchesPointeuse(a.BddPointeuse))
            .ToList();
        return new PlanningTargets(cards, categories, affectations);
    }

    public CardPaie? Carte(string matricule) =>
        _cards.FirstOrDefault(c => MatriculeIdentity.MemeIdentifiant(c.SageMatricule, matricule));

    public Categorie? Categorie(string intitule, bool ignorerEspaces) =>
        _categories.FirstOrDefault(c =>
            string.Equals(ignorerEspaces ? c.Intitule?.Trim() : c.Intitule, intitule, StringComparison.OrdinalIgnoreCase));

    /// <summary>Cartes affectées à la catégorie (<c>T_CAT_SAL.MATRICULE_SAGE</c> = <c>T_CARDPAIE.ID</c>).</summary>
    public List<CardPaie> Affectees(Categorie categorie)
    {
        var ids = _affectations
            .Where(a => a.IdCateg == categorie.IdCategorie)
            .Select(a => a.MatriculeSage)
            .ToHashSet();
        return _cards.Where(c => ids.Contains(c.Id)).ToList();
    }
}
