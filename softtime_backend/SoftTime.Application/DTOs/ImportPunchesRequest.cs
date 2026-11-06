namespace SoftTime.Application.DTOs;

public record ImportPunchesRequest(string? MatriculeFrom, string? MatriculeTo, DateTime From, DateTime To)
{
    public ImportPunchesRequest() : this(default, default, default, default) { }
}
