namespace SoftTime.Application.DTOs;

/// <summary>
/// Période de Mon espace (jours inclusifs), après bornage. Les réponses de Mon espace n'exposent ni identifiant de
/// table, ni matricule, ni nom, et jamais les données d'un autre salarié.
/// </summary>
public sealed record SelfPeriodDto(DateTime From, DateTime To);
