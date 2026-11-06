namespace SoftTime.Application.DTOs;

public record HistoryDeleteResultDto(int Supprimees, string Message)
{
    public HistoryDeleteResultDto() : this(default, default!) { }
}
