namespace SoftTime.Application.DTOs;

public record WeeklyValidationRequest(
    string? MatriculeFrom,
    string? MatriculeTo,
    DateTime From,
    DateTime To,
    IReadOnlyList<string>? SelectedMatricules,
    string? Departement = null) : PeriodRequest(MatriculeFrom, MatriculeTo, From, To, Departement)
{
    public WeeklyValidationRequest() : this(default, default, default, default, default, default) { }
}
