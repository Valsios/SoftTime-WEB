namespace SoftTime.Application.Abstractions.Permissions;

/// <summary>
/// Journal des modifications de droits (T_AUDIT_DROIT) au nom du compte de la requête : acteur, cible, action, avant,
/// après et date (UTC). Ajoute la ligne à l'unité de travail : elle est enregistrée avec la modification elle-même.
/// </summary>
public interface ICurrentOperatorRightsJournal
{
    Task RecordAsync(RightsTarget target, string action, IEnumerable<string>? before, IEnumerable<string>? after, CancellationToken ct);
}
