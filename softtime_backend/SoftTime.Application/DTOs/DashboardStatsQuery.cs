namespace SoftTime.Application.DTOs;

/// <summary>Filtres du tableau de bord : période, plage de matricules, département, catégorie et granularité.</summary>
public sealed record DashboardStatsQuery(
    DateTime From,
    DateTime To,
    string? MatriculeFrom = null,
    string? MatriculeTo = null,
    string? Departement = null,
    string? Categorie = null,
    string? Periode = null)
{
    private const string GranulariteParDefaut = "mensuelle";

    /// <summary>Période et population filtrées.</summary>
    public PeriodRequest ToPeriodRequest() => new(MatriculeFrom, MatriculeTo, From, To, Departement);

    /// <summary>Granularité demandée, mensuelle par défaut.</summary>
    public string Granularite => string.IsNullOrWhiteSpace(Periode) ? GranulariteParDefaut : Periode;
}
