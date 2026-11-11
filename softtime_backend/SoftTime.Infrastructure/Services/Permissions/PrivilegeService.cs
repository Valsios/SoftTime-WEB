using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Permissions;

public sealed class PrivilegeService : IPrivilegeService
{
    private static readonly HashSet<decimal> s_knownGates = new()
    {
        Rights.Databases, Rights.Parameters, Rights.Processing, Rights.Other, Rights.Traceability, Rights.Reports
    };

    private readonly IUnitOfWork _uow;
    private readonly IEffectiveRightsService _effectiveRights;
    private readonly IParametersAccessGuard _parametersAccessGuard;
    private readonly IRightsChangeJournal _rightsChangeJournal;

    public PrivilegeService(
        IUnitOfWork uow,
        IEffectiveRightsService effectiveRights,
        IParametersAccessGuard parametersAccessGuard,
        IRightsChangeJournal rightsChangeJournal)
    {
        _uow = uow;
        _effectiveRights = effectiveRights;
        _parametersAccessGuard = parametersAccessGuard;
        _rightsChangeJournal = rightsChangeJournal;
    }

    public IReadOnlyList<PermissionCategoryDto> GetCatalog()
        => PermissionCatalog.Categories
            .Select(c => new PermissionCategoryDto(
                c.Key,
                c.Title,
                c.Gate,
                c.Items.Select(i => new PermissionItemDto(i.Code, i.Label, i.CanCreate, i.CanUpdate, i.CanDelete)).ToList()))
            .ToList();

    public async Task<IReadOnlyList<DroitDto>> GetDroitsAsync(CancellationToken cancellationToken = default)
    {
        await _parametersAccessGuard.EnsureCanReadAsync(ReadPermissions.PrivilegeList, cancellationToken);
        return (await _uow.Repository<DroitAcces>().ListReadOnlyAsync(_ => true, cancellationToken))
            .Select(d => new DroitDto(d.IdDroit, d.Droit)).ToList();
    }

    public async Task<IReadOnlyList<PrivilegeDto>> GetPrivilegesAsync(decimal? roleId, CancellationToken cancellationToken = default)
    {
        await _parametersAccessGuard.EnsureCanReadAsync(ReadPermissions.PrivilegeList, cancellationToken);
        var list = await _uow.Repository<DroitRole>().ListReadOnlyAsync(
            p => !roleId.HasValue || p.IdRole == roleId.Value, cancellationToken);
        return list.Select(p => new PrivilegeDto(p.IdDroitRole, p.IdRole, p.IdDroit)).ToList();
    }

    public async Task SetPrivilegesAsync(decimal roleId, IReadOnlyCollection<decimal> droitIds, CancellationToken cancellationToken = default)
    {
        _parametersAccessGuard.RequireAdministrator();
        if (roleId == AdminRules.AdminRoleId)
            throw new DomainException("Les privilèges du rôle Administrateur ne sont pas modifiables.");
        var gates = ValidateGates(droitIds);
        var role = await _uow.Repository<Role>().GetByIdAsync(roleId, cancellationToken)
            ?? throw new NotFoundException("Rôle introuvable.");
        // Les portes d'un rôle ayant des droits détaillés se déduisent de ces droits (SaveRoleDetailedPermissionsAsync) : écrire
        // T_DROIT_ROLE à la main n'aurait aucun effet sur le jeton et le contredirait.
        if ((await _uow.Repository<RolePermission>().ListAsync(p => p.IdRole == roleId, cancellationToken)).Count > 0)
            throw new DomainException("Ce rôle a des droits détaillés : modifiez-les depuis la page Privilèges, les portes en sont déduites.");

        var repo = _uow.Repository<DroitRole>();
        var existing = await repo.ListAsync(p => p.IdRole == roleId, cancellationToken);
        repo.RemoveRange(existing);
        foreach (var gate in gates)
            await repo.AddAsync(new DroitRole { IdRole = roleId, IdDroit = gate }, cancellationToken);
        await _rightsChangeJournal.RecordAsync(
            new RightsChangeEntry(
                RightsChangeActions.RoleTarget, roleId, role.NomRole, RightsChangeActions.UpdateRoleGates,
                GateCodes(existing.Select(e => e.IdDroit)), GateCodes(gates)),
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        _effectiveRights.InvalidateAll();
    }

    private static List<decimal> ValidateGates(IReadOnlyCollection<decimal>? droitIds)
    {
        var gates = (droitIds ?? Array.Empty<decimal>()).Distinct().OrderBy(g => g).ToList();
        var unknown = gates.Where(g => !s_knownGates.Contains(g)).ToList();
        if (unknown.Count > 0)
            throw new DomainException($"Droits inconnus : {string.Join(", ", unknown)}.");
        return gates;
    }

    private static IEnumerable<string> GateCodes(IEnumerable<decimal> gates)
        => gates.OrderBy(g => g).Select(g => $"GATE:{g:0}").ToList();
}
