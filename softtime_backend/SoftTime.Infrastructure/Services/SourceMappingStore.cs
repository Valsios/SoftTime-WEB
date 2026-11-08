using AutoMapper;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

public sealed class SourceMappingStore : ISourceMappingStore
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly IDatabaseConfigurationAuthorizer _configAuth;

    public SourceMappingStore(
        IUnitOfWork uow,
        IMapper mapper,
        IDatabaseConfigurationAuthorizer configAuth)
    {
        _uow = uow;
        _mapper = mapper;
        _configAuth = configAuth;
    }

    // Supprime les mappings d'entite (et leurs champs) d'une connexion. Sans cela les lignes
    // restent orphelines et bloquent la reutilisation de l'index unique (SageDbId/PointeuseDbId, EntityKind).
    public async Task RemoveMappingsAsync(bool isSage, int connectionId, CancellationToken ct)
    {
        var entRepo = _uow.Repository<SourceEntityMapping>();
        var fldRepo = _uow.Repository<SourceFieldMapping>();
        var existing = await entRepo.ListAsync(m => isSage ? m.SageDbId == connectionId : m.PointeuseDbId == connectionId, ct);
        if (existing.Count == 0) return;
        var ids = existing.Select(x => x.Id).ToHashSet();
        var fields = await fldRepo.ListAsync(f => ids.Contains(f.EntityMappingId), ct);
        fldRepo.RemoveRange(fields);
        entRepo.RemoveRange(existing);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Mot de passe à utiliser pour tester ou explorer une connexion. Un mot de passe saisi l'emporte.
    /// Sinon, pour une base existante, le mot de passe stocké (jamais renvoyé au client) n'est utilisé que si
    /// l'appelant peut modifier cette base ET que serveur, login et mode d'authentification sont inchangés :
    /// le mot de passe stocké ne doit jamais être envoyé vers un autre serveur choisi par le client.
    /// </summary>
    public async Task<string?> ResolveStoredPasswordAsync(ConnectionAttempt saisie, CancellationToken ct)
    {
        var (kind, id, serveur, sqlAuth, login, password) = saisie;
        if (!string.IsNullOrEmpty(password) || id is not > 0 || sqlAuth == false) return password;
        var isSage = string.Equals(kind, "SAGE", StringComparison.OrdinalIgnoreCase);
        if (!isSage && !string.Equals(kind, "POINTEUSE", StringComparison.OrdinalIgnoreCase)) return password;

        var (stored, storedName) = isSage
            ? Enregistree(await _uow.Repository<BddSage>().GetByIdAsync(id.Value, ct) ?? throw new NotFoundException())
            : Enregistree(await _uow.Repository<BddPointeuse>().GetByIdAsync(id.Value, ct) ?? throw new NotFoundException());

        await _configAuth.EnsureCanConfigureAsync(isSage ? DatabaseKind.Sage : DatabaseKind.Pointeuse, DatabaseConfigAction.Update, storedName, ct);
        var sameTarget = stored.SqlAuth
                         && string.Equals((serveur ?? string.Empty).Trim(), (stored.Serveur ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
                         && string.Equals((login ?? string.Empty).Trim(), (stored.Login ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        if (!sameTarget)
            throw new DomainException("Le serveur, le login ou le mode d'authentification a changé : saisissez le mot de passe pour utiliser cette connexion.");
        return stored.Password;
    }

    private static (SqlServerCredentials Stored, string? Nom) Enregistree(BddSage row) => (SqlServerCredentials.Of(row), row.NomBd);

    private static (SqlServerCredentials Stored, string? Nom) Enregistree(BddPointeuse row) => (SqlServerCredentials.Of(row), row.NomBd);

    public async Task ReplaceMappingsAsync(bool isSage, int connectionId, IReadOnlyList<SourceEntityMappingDto> mappings, CancellationToken ct)
    {
        var entRepo = _uow.Repository<SourceEntityMapping>();
        var fldRepo = _uow.Repository<SourceFieldMapping>();
        var existing = await entRepo.ListAsync(m => isSage ? m.SageDbId == connectionId : m.PointeuseDbId == connectionId, ct);
        if (existing.Count > 0)
        {
            var ids = existing.Select(e => e.Id).ToHashSet();
            var existingFields = await fldRepo.ListAsync(f => ids.Contains(f.EntityMappingId), ct);
            fldRepo.RemoveRange(existingFields);
            entRepo.RemoveRange(existing);
            await _uow.SaveChangesAsync(ct);
        }
        foreach (var m in mappings.Where(x => !string.IsNullOrWhiteSpace(x.EntityKind)))
        {
            var ent = new SourceEntityMapping
            {
                SystemType = isSage ? "SAGE" : "POINTEUSE",
                SageDbId = isSage ? connectionId : null,
                PointeuseDbId = isSage ? null : connectionId,
                EntityKind = m.EntityKind.Trim(),
                SourceTable = string.IsNullOrWhiteSpace(m.SourceTable) ? null : m.SourceTable!.Trim()
            };
            await entRepo.AddAsync(ent, ct);
            await _uow.SaveChangesAsync(ct);
            foreach (var f in (m.Fields ?? Array.Empty<SourceFieldMappingDto>()).Where(x => !string.IsNullOrWhiteSpace(x.FieldRoleCode)))
            {
                await fldRepo.AddAsync(new SourceFieldMapping
                {
                    EntityMappingId = ent.Id,
                    FieldRoleCode = f.FieldRoleCode.Trim(),
                    SourceColumn = string.IsNullOrWhiteSpace(f.SourceColumn) ? null : f.SourceColumn!.Trim()
                }, ct);
            }
        }
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SourceEntityMappingDto>> LoadMappingsForAsync(bool isSage, int connectionId, CancellationToken ct)
    {
        var lookup = await LoadMappingLookupAsync(isSage, ct);
        return lookup.TryGetValue(connectionId, out var list) ? list : Array.Empty<SourceEntityMappingDto>();
    }

    public async Task<Dictionary<int, List<SourceEntityMappingDto>>> LoadMappingLookupAsync(bool isSage, CancellationToken ct)
    {
        var entities = await _uow.Repository<SourceEntityMapping>().ListAsync(_ => true, ct);
        var fields = await _uow.Repository<SourceFieldMapping>().ListAsync(_ => true, ct);
        var fieldsByEntity = fields.GroupBy(f => f.EntityMappingId).ToDictionary(g => g.Key, g => g.ToList());
        var result = new Dictionary<int, List<SourceEntityMappingDto>>();
        foreach (var e in entities)
        {
            var key = isSage ? e.SageDbId : e.PointeuseDbId;
            if (key is not int id) continue;
            var dto = _mapper.Map<SourceEntityMappingDto>(e) with
            {
                Fields = (fieldsByEntity.TryGetValue(e.Id, out var fs) ? fs : new List<SourceFieldMapping>())
                    .Select(_mapper.Map<SourceFieldMappingDto>)
                    .ToList()
            };
            if (!result.TryGetValue(id, out var list)) { list = new List<SourceEntityMappingDto>(); result[id] = list; }
            list.Add(dto);
        }
        return result;
    }

    /// <summary>
    /// Variante ciblée : ne charge que les mappings des connexions de la page courante, pour éviter
    /// de relire toute la table à chaque page. Les champs sont limités aux mappings sélectionnés.
    /// </summary>
    public async Task<Dictionary<int, List<SourceEntityMappingDto>>> LoadMappingLookupAsync(
        bool isSage, IReadOnlyCollection<int> connectionIds, CancellationToken ct)
    {
        if (connectionIds.Count == 0)
            return new Dictionary<int, List<SourceEntityMappingDto>>();

        var ids = connectionIds.Distinct().ToList();
        var entities = isSage
            ? await _uow.Repository<SourceEntityMapping>().ListAsync(
                e => e.SageDbId.HasValue && ids.Contains(e.SageDbId.Value), ct)
            : await _uow.Repository<SourceEntityMapping>().ListAsync(
                e => e.PointeuseDbId.HasValue && ids.Contains(e.PointeuseDbId.Value), ct);
        if (entities.Count == 0)
            return new Dictionary<int, List<SourceEntityMappingDto>>();

        var entityIds = entities.Select(e => e.Id).ToList();
        var fields = await _uow.Repository<SourceFieldMapping>().ListAsync(
            f => entityIds.Contains(f.EntityMappingId), ct);
        var fieldsByEntity = fields.GroupBy(f => f.EntityMappingId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new Dictionary<int, List<SourceEntityMappingDto>>();
        foreach (var e in entities)
        {
            var key = isSage ? e.SageDbId : e.PointeuseDbId;
            if (key is not int id) continue;
            var dto = _mapper.Map<SourceEntityMappingDto>(e) with
            {
                Fields = (fieldsByEntity.TryGetValue(e.Id, out var fs) ? fs : new List<SourceFieldMapping>())
                    .Select(_mapper.Map<SourceFieldMappingDto>)
                    .ToList()
            };
            if (!result.TryGetValue(id, out var list)) { list = new List<SourceEntityMappingDto>(); result[id] = list; }
            list.Add(dto);
        }
        return result;
    }
}
