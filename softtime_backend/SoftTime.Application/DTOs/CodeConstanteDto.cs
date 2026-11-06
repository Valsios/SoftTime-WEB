namespace SoftTime.Application.DTOs;

public record CodeConstanteDto(int Id, string Categorie, string? Intitule, string CodeConstante, string? TableCible = null)
{
    public CodeConstanteDto() : this(default, default!, default, default!) { }
}
