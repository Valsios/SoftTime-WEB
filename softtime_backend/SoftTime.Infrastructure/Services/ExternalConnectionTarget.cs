using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Services;

/// <summary>Base externe à tester (mot de passe déjà résolu depuis la base enregistrée si besoin).</summary>
internal sealed record ExternalConnectionTarget(
    SqlServerCredentials Credentials, string NomBd, string TypeBase, IReadOnlyList<SourceEntityMappingDto>? Mappings);
