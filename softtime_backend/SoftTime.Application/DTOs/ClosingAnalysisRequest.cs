namespace SoftTime.Application.DTOs;

/// <summary>
/// Demande d'analyse de clôture : période inclusive, département facultatif, page de la liste
/// d'anomalies et options des règles (valeurs par défaut du Domain si absentes).
/// Sans <see cref="From"/> ni <see cref="To"/>, la période est le mois civil complet précédent.
/// <see cref="IncludeComparison"/> (faux par défaut) charge et calcule aussi la période précédente
/// de même longueur ; sinon <c>Comparaison</c> vaut <c>null</c>. <see cref="IncludeInformation"/>
/// (faux par défaut) ajoute à la LISTE les anomalies « Information » ; les comptages les incluent toujours.
/// <see cref="ReuseCached"/> (faux par défaut) autorise la réutilisation, pendant un court instant, de l'analyse
/// déjà calculée pour la même période : réservé aux changements de page, jamais à une nouvelle analyse.
/// </summary>
public sealed record ClosingAnalysisRequest(
    DateTime From,
    DateTime To,
    string? Departement = null,
    int? Page = null,
    int? PageSize = null,
    ClosingRulesOptions? Options = null,
    bool IncludeComparison = false,
    bool IncludeInformation = false,
    bool ReuseCached = false);
