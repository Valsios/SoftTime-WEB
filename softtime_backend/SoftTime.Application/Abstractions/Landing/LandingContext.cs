namespace SoftTime.Application.Abstractions.Landing;

/// <summary>Données d'une société prêtes pour le calcul d'atterrissage.</summary>
/// <param name="Employees">Salariés actifs de la pointeuse courante, une entrée par matricule.</param>
/// <param name="DernierPointage">Jour du dernier pointage traité de la société (aucun : <c>null</c>).</param>
public sealed record LandingContext(IReadOnlyList<LandingEmployee> Employees, DateTime? DernierPointage);
