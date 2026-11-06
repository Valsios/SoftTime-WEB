namespace SoftTime.Application.DTOs;

public record DroitDto(decimal Id, string Nom)
{
    public DroitDto() : this(default, default!) { }
}
