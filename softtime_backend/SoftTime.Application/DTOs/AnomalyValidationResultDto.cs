namespace SoftTime.Application.DTOs;

public record AnomalyValidationResultDto(int CorrectionsCreees, int AnomaliesSupprimees, string Message)
{
    public AnomalyValidationResultDto() : this(default, default, default!) { }
}
