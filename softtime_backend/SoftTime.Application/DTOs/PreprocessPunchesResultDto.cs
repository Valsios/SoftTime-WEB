namespace SoftTime.Application.DTOs;

/// <summary>Résultat du pré-traitement des pointages (pointage → heures corrigées + anomalies).</summary>
public record PreprocessPunchesResultDto(int CorrectionsCreees, int AnomaliesCreees, int JoursIgnores, string Message)
{
    public PreprocessPunchesResultDto() : this(default, default, default, default!) { }
}
