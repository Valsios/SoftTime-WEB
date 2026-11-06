namespace SoftTime.Application.DTOs;

/// <summary>Résultat de la purge des validations d'une semaine.</summary>
public record PurgeValidationResultDto(int Validations, int Corrections, string Message)
{
    public PurgeValidationResultDto() : this(default, default, default!) { }
}
