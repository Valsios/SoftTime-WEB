namespace SoftTime.Application.DTOs;

public record ImportResultDto(int Imported, int Skipped, string Message)
{
    public ImportResultDto() : this(default, default, default!) { }
}
