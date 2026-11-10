namespace SoftTime.Application.DTOs;

/// <summary>Validation en masse : les anomalies sélectionnées deviennent des heures corrigées.</summary>
public record AnomalyValidationRequest(List<decimal> Ids)
{
    public AnomalyValidationRequest() : this(new List<decimal>()) { }
}
