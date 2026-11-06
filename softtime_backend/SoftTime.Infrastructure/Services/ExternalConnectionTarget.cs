using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Services;

/// <summary>Base externe à tester (mot de passe déjà résolu depuis la base enregistrée si besoin).</summary>
internal sealed record ExternalConnectionTarget(
    string Serveur, string NomBd, bool SqlAuth, string? Login, string? Password, string TypeBase,
    IReadOnlyList<SourceEntityMappingDto>? Mappings);
