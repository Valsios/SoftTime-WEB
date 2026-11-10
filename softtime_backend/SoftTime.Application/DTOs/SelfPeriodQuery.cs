namespace SoftTime.Application.DTOs;

/// <summary>Période demandée dans Mon espace (jours inclusifs) ; absente : le mois en cours jusqu'à
/// aujourd'hui.</summary>
public sealed record SelfPeriodQuery(DateTime? From = null, DateTime? To = null);
