namespace SoftTime.Application.DTOs;

public record ReportFilter(string? MatriculeFrom, string? MatriculeTo, DateTime From, DateTime To, string? Departement, string? Search = null)
{
    public ReportFilter() : this(default, default, default, default, default) { }
}
