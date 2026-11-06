namespace SoftTime.Application.DTOs;

public record CategoryDto(int Id, string? Intitule, int SupervisorCardId, TimeSpan? Pause, decimal? HeuresSemaine)
{
    public CategoryDto() : this(default, default, default, default, default) { }
}
