namespace SoftTime.Domain.Models;

/// <summary>Période demandée d'un état ; elle est plafonnée à la veille de <c>DateDuJour</c> (aucun jour futur).</summary>
public sealed record ReportPeriod(DateTime From, DateTime To, DateTime DateDuJour);
