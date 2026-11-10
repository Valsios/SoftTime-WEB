namespace SoftTime.Application.DTOs;

/// <summary>Création du premier compte administrateur (installation vierge).</summary>
public record AdminSetupDto(string Nom, string Login, string Password, string? Email)
{
    public AdminSetupDto() : this(string.Empty, string.Empty, string.Empty, null) { }
}
