namespace SoftTime.Domain.Models;

/// <summary>Période demandée d'un état ; elle est plafonnée à la veille de <c>DateDuJour</c> (aucun jour futur).</summary>
public sealed record ReportPeriod(DateTime From, DateTime To, DateTime DateDuJour)
{
    public DateTime Debut => From.Date;

    /// <summary>Fin effective : la fin demandée, sans dépasser la veille.</summary>
    public DateTime Fin => To.Date < DateDuJour.Date.AddDays(-1) ? To.Date : DateDuJour.Date.AddDays(-1);

    /// <summary>Période sans aucun jour passé à traiter.</summary>
    public bool EstVide => Debut > Fin;
}
