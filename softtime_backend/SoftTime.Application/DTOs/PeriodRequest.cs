namespace SoftTime.Application.DTOs;

public record PeriodRequest(string? MatriculeFrom, string? MatriculeTo, DateTime From, DateTime To, string? Departement = null, string? Search = null)
{
    public PeriodRequest() : this(default, default, default, default, default, default) { }
}
