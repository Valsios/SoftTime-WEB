namespace SoftTime.Application.DTOs;

public record PayrollWriteConfigurationDto(int Id, string? TableCible, string? ColMatricule, string Categorie, string? ColValeur)
{
    public PayrollWriteConfigurationDto() : this(default, default, default, "Exo130", default) { }
}
