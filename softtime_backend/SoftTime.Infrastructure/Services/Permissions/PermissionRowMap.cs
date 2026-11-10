using System.Linq.Expressions;

namespace SoftTime.Infrastructure.Services.Permissions;

/// <summary>
/// Accès uniforme aux lignes de permission d'une table (rôles ou comptes), pour <see cref="PermissionTable{TRow}"/>.
/// </summary>
internal sealed record PermissionRowMap<TRow>
{
    /// <summary>Propriétaire de la ligne (identifiant du rôle ou du compte).</summary>
    public required Func<TRow, decimal> Owner { get; init; }

    public required Func<TRow, string> Code { get; init; }

    public required Action<TRow, string> SetCode { get; init; }

    /// <summary>Filtre traduisible en SQL : code commençant par le texte donné.</summary>
    public required Func<string, Expression<Func<TRow, bool>>> CodeStartsWith { get; init; }

    /// <summary>Filtre traduisible en SQL : lignes des propriétaires donnés.</summary>
    public required Func<List<decimal>, Expression<Func<TRow, bool>>> OwnedBy { get; init; }

    /// <summary>Ligne « aucun droit » d'un propriétaire.</summary>
    public required Func<decimal, TRow> EmptyMarker { get; init; }
}
