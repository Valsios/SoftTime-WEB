namespace SoftTime.Application.DTOs;

/// <summary>Résultat de la purge d'une période de traitement.</summary>
public record PurgePunchesResultDto(int Pointages, int Corrections, int Anomalies, int CorrectionsManuellesConservees, string Message)
{
    public PurgePunchesResultDto() : this(default, default, default, default, default!) { }
}
