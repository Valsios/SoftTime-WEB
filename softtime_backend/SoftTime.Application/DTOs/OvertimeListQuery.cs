namespace SoftTime.Application.DTOs;

/// <summary>Période, plage de matricules, département, recherche et page des cumuls EXO/IMPO (paramètres de requête).</summary>
public sealed record OvertimeListQuery(
    DateTime From,
    DateTime To,
    string? MatriculeFrom = null,
    string? MatriculeTo = null,
    string? Departement = null,
    int? Page = null,
    int? PageSize = null,
    string? Search = null)
{
    /// <summary>Période et population filtrées ; la recherche n'est reprise que pour la liste paginée.</summary>
    public PeriodRequest ToPeriodRequest(bool withSearch) =>
        new(MatriculeFrom, MatriculeTo, From, To, Departement, withSearch ? Search : null);
}
