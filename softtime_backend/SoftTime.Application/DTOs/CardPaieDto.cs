namespace SoftTime.Application.DTOs;

public record CardPaieDto(
    int Id, string? SageMatricule, string? SageNom, string? SagePrenom,
    string? PointeuseNumero, string? PointeuseNom, string? SageServeur, string? PointeuseServeur,
    string? SageBdd, string? PointeuseBdd, DateTime? Date,
    string Origine = "AUTO", bool Actif = true, string? Departement = null)
{
    public CardPaieDto() : this(default, default, default, default, default, default, default, default, default, default, default) { }
}
