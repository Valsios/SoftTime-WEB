using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Services.Dashboard;

/// <summary>
/// Cartes retenues par les filtres de la page et leur rattachement aux catégories de personnel.
/// <c>T_CAT_SAL.MATRICULE_SAGE</c> référence en réalité <c>T_CARDPAIE.ID</c>.
/// </summary>
internal sealed record DashboardPerimetre(
    List<CardPaie> Cartes,
    Dictionary<int, int> CategorieParCarte,
    Dictionary<int, Categorie> CategoriesParId);
