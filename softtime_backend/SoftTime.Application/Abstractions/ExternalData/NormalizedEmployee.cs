namespace SoftTime.Application.Abstractions.ExternalData;

// La fiche "Salarie"
public record NormalizedEmployee(
    string Matricule,
    string? Nom,
    string? Prenom,
    string? NumeroBadge,
    bool Desactive,
    string? CleInterne); // equivalent de SA_CompteurNumero ; peut etre absent (base "Autre")
