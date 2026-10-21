namespace SoftTime.Domain.Services;

/// <summary>
/// Helper commun pour la pagination backend.
/// </summary>
public static class PaginationHelper
{
    /// <summary>
    /// Calcule et normalise la page et la taille de page.
    /// Méthode unique d'isolation pour le calcul de taille :
    /// POINT D'INSERTION FUTUR pour la règle « consulter la totalité à la demande » (ex: pageSize = -1 ou 0).
    /// En l'attente de la confirmation de cette règle, les plafonds de PaginationRules s'appliquent strictement.
    /// </summary>
    /// <param name="page">Numéro de page demandé (1-based, défaut 1 si null ou &lt; 1).</param>
    /// <param name="pageSize">Taille de page demandée (défaut 50 si null ou &lt; 1, plafonnée à 100).</param>
    /// <returns>Couple normalisé (Page, PageSize).</returns>
    public static (int Page, int PageSize) CalculerPagination(int? page, int? pageSize)
    {
        // -----------------------------------------------------------------------------------------
        // POINT D'INSERTION DE LA RÈGLE « CONSULTER LA TOTALITÉ À LA DEMANDE » :
        // Dès confirmation de la convention (ex. if (pageSize == -1) return (1, int.MaxValue)),
        // la condition sera ajoutée ici même, sans toucher au reste de l'architecture.
        // -----------------------------------------------------------------------------------------

        return PaginationRules.Normaliser(page, pageSize);
    }
}
