using SoftTime.Application.DTOs;
using SoftTime.Application.Security;

namespace SoftTime.Application.Abstractions.Audit;

/// <summary>
/// Lecture seule du journal central <c>suivi_JOURNAL</c> (base de l'application, indépendante de la base SAGE choisie).
/// Accès : administrateur, ou porte 5 ET permission <c>audit.read</c> effectives. Les valeurs sensibles sont
/// masquées à la lecture par <see cref="AuditMaskingPolicy"/>, même si une ancienne ligne a été stockée en clair.
/// Ce service n'écrit jamais : aucune modification ni suppression du journal n'est exposée.
/// </summary>
public interface IJournalService
{
    Task<JournalPageDto> ListAsync(JournalQuery query, CancellationToken ct = default);

    Task<JournalEntryDto> GetAsync(long id, CancellationToken ct = default);

    Task<JournalFacetsDto> GetFacetsAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
}
