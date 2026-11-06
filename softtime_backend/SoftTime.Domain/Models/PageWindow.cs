using SoftTime.Domain.Services;

namespace SoftTime.Domain.Models;

/// <summary>
/// Fenêtre d'une page sur un total de lignes : page et taille normalisées et nombre de pages. La page
/// est vide sans aucune ligne ou au-delà de la dernière page.
/// </summary>
public sealed record PageWindow(int Total, int Page, int Taille, int TotalPages)
{
    public bool EstVide => Total == 0 || Page > TotalPages;

    public int Decalage => PaginationRules.Decalage(Page, Taille);

    public static PageWindow Pour(int total, int? page, int? pageSize)
    {
        var (pageNorm, taille) = PaginationRules.Normaliser(page, pageSize);
        return new PageWindow(total, pageNorm, taille, PaginationRules.NombreDePages(total, taille));
    }

    public PagedResult<T> Resultat<T>(IReadOnlyList<T> items) => new(items, Total, Page, Taille, TotalPages);
}
