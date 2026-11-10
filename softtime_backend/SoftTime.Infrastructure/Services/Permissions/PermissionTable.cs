using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Permissions;

/// <summary>
/// Lignes de permission d'une table (rôles ou comptes) vues de façon uniforme pour suivre les codes d'une base.
/// Un propriétaire qui n'a plus aucune ligne reçoit le marqueur « aucun droit » : sans lui, la règle héritée lui
/// rendrait les droits par défaut de ses portes (par exemple la lecture de toutes les bases).
/// </summary>
internal sealed class PermissionTable<TRow> where TRow : class
{
    private static readonly string[] Actions = { "READ", "UPDATE", "DELETE", "CREATE" };

    private readonly IRepository<TRow> _repo;
    private readonly PermissionRowMap<TRow> _map;

    public PermissionTable(IRepository<TRow> repo, PermissionRowMap<TRow> map)
    {
        _repo = repo;
        _map = map;
    }

    /// <summary>
    /// Renomme les codes de la base ; si le propriétaire a déjà le nouveau code, l'ancienne ligne est retirée.
    /// </summary>
    public async Task RenameAsync(string prefix, string oldName, string newName, CancellationToken ct)
    {
        var rows = await _repo.ListAsync(_map.CodeStartsWith($"{prefix}:"), ct);
        foreach (var row in rows.Where(r => ActionOf(_map.Code(r), prefix, oldName) is not null).ToList())
        {
            var renamed = $"{prefix}:{newName}:{ActionOf(_map.Code(row), prefix, oldName)}";
            var alreadyHeld = rows.Any(r => _map.Owner(r) == _map.Owner(row)
                && string.Equals(_map.Code(r), renamed, StringComparison.OrdinalIgnoreCase));
            if (alreadyHeld)
            {
                _repo.Remove(row);
                continue;
            }
            _map.SetCode(row, renamed);
            _repo.Update(row);
        }
    }

    /// <summary>
    /// Retire les codes de la base ; marqueur « aucun droit » pour un propriétaire qui n'a plus de ligne.
    /// </summary>
    public async Task RemoveAsync(string prefix, string name, CancellationToken ct)
    {
        var removed = (await _repo.ListAsync(_map.CodeStartsWith($"{prefix}:"), ct))
            .Where(r => ActionOf(_map.Code(r), prefix, name) is not null)
            .ToList();
        if (removed.Count == 0) return;
        _repo.RemoveRange(removed);

        var owners = removed.Select(_map.Owner).Distinct().ToList();
        var remaining = (await _repo.ListAsync(_map.OwnedBy(owners), ct))
            .Except(removed)
            .Select(_map.Owner)
            .ToHashSet();
        foreach (var owner in owners.Where(o => !remaining.Contains(o)))
            await _repo.AddAsync(_map.EmptyMarker(owner), ct);
    }

    /// <summary>
    /// Action (READ, UPDATE…) si le code vise exactement cette base (nom sans tenir compte de la casse), sinon null.
    /// </summary>
    private static string? ActionOf(string code, string prefix, string name)
    {
        var start = $"{prefix}:{name}:";
        if (!code.StartsWith(start, StringComparison.OrdinalIgnoreCase)) return null;
        var action = code[start.Length..].ToUpperInvariant();
        return Actions.Contains(action) ? action : null;
    }
}
