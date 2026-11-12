using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services.DataSources;

namespace SoftTime.Tests;

/// <summary>Lot 6 : les codes SAGE:&lt;nom&gt;:… suivent le renommage d'une base SAGE et disparaissent à sa suppression.</summary>
public class SageDatabasePermissionCodesTests : DatabasePermissionCodesTests
{
    public SageDatabasePermissionCodesTests() : base("SAGE")
    {
        Uow.Get<BddSage>().Items.Add(new BddSage { Id = 7, NomBd = "PAIE_2025", Serveur = "srv", TLogin = "sa", TMdp = "FAKE", TypeAuth = true });
        Uow.Get<BddSage>().Items.Add(new BddSage { Id = 8, NomBd = "AUTRE", Serveur = "srv", TLogin = "sa", TMdp = "FAKE", TypeAuth = true });
    }

    protected override Task Rename(string newName) => Sut.Sage.SaveSageAsync(new SageDbDto(7, "srv", "sa", null, true, newName));

    protected override Task Delete() => Sut.Sage.DeleteSageAsync(7);
}

/// <summary>Lot 7 : même règle pour les codes POINTEUSE:&lt;nom&gt;:… des bases pointeuse.</summary>
public class PointeuseDatabasePermissionCodesTests : DatabasePermissionCodesTests
{
    public PointeuseDatabasePermissionCodesTests() : base("POINTEUSE")
    {
        Uow.Get<BddPointeuse>().Items.Add(new BddPointeuse { Id = 7, NomBd = "PAIE_2025", Serveur = "srv", TLogin = "sa", TMdp = "FAKE", TypeAuth = true });
        Uow.Get<BddPointeuse>().Items.Add(new BddPointeuse { Id = 8, NomBd = "AUTRE", Serveur = "srv", TLogin = "sa", TMdp = "FAKE", TypeAuth = true });
    }

    protected override Task Rename(string newName)
        => Sut.Pointeuse.SavePointeuseAsync(new PointeuseDbDto(7, "srv", "sa", null, true, newName, null, null));

    protected override Task Delete() => Sut.Pointeuse.DeletePointeuseAsync(7);
}

/// <summary>
/// Scénarios communs aux bases SAGE et pointeuse : les noms de base sont identiques (PAIE_2025 renommée, AUTRE
/// intacte) ; seul le préfixe des codes change.
/// </summary>
public abstract class DatabasePermissionCodesTests
{
    private readonly string _prefix;
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

    protected DatabasePermissionCodesTests(string prefix)
    {
        _prefix = prefix;
        var auth = new AuthServices(Uow, ServiceMocks.TokenService().Object, _mapper, ServiceMocks.EmailService().Object,
            new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(Uow), new PasswordHasher<Responsable>());
        Sut = new CatalogServices(Uow, _mapper, null!, null!, SoftTime.Tests.Http.NullObject.Create<IExternalDiscoveryService>(),
            new DatabaseConfigurationAuthorizer(new TestCurrentUser(1, 1), auth), SoftTime.Tests.Http.TestRights.Service(Uow));

        Uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        Uow.Get<Role>().Items.Add(new Role { IdRole = 5, NomRole = "Gestionnaire" });
        Uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin" });
        Uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 2, IdRole = 5, Login = "gestionnaire" });
    }

    protected InMemoryUnitOfWork Uow { get; } = new();

    private protected CatalogServices Sut { get; }

    protected abstract Task Rename(string newName);

    protected abstract Task Delete();

    /// <summary>« SAGE:BASE:READ » devient « &lt;préfixe&gt;:BASE:READ » : mêmes scénarios pour les deux types de base.</summary>
    private string Coded(string code) => code.StartsWith("SAGE:", StringComparison.Ordinal) ? _prefix + code[4..] : code;

    private void Role(params string[] codes)
    {
        foreach (var code in codes)
            Uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 5, PermissionCode = Coded(code), IsGranted = true });
    }

    private void User(params string[] codes)
    {
        foreach (var code in codes)
            Uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 2, PermissionCode = Coded(code), IsGranted = true });
    }

    private List<string> RoleCodes() => Uow.Get<RolePermission>().Items.Where(p => p.IdRole == 5).Select(p => p.PermissionCode).ToList();

    private List<string> UserCodes() => Uow.Get<UserPermission>().Items.Where(p => p.IdResponsable == 2).Select(p => p.PermissionCode).ToList();

    [Fact]
    public async Task Save_Rename_RightsFollowTheNewNameInOneSave()
    {
        // Arrange
        Role("PROC:SHIFTS", "SAGE:PAIE_2025:READ", "SAGE:AUTRE:READ");
        User("SAGE:PAIE_2025:READ", "SAGE:PAIE_2025:UPDATE", "SAGE:paie_2025:delete");
        Uow.ResetSaveChangesCount();

        // Act
        await Rename("PAIE_2026");

        // Assert
        RoleCodes().Should().BeEquivalentTo("PROC:SHIFTS", Coded("SAGE:PAIE_2026:READ"), Coded("SAGE:AUTRE:READ"));
        UserCodes().Should().BeEquivalentTo(Coded("SAGE:PAIE_2026:READ"), Coded("SAGE:PAIE_2026:UPDATE"), Coded("SAGE:PAIE_2026:DELETE"));
        Uow.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Save_RenameWhenNewCodeAlreadyHeld_KeepsASingleRow()
    {
        // Arrange
        User("SAGE:PAIE_2025:READ", "SAGE:PAIE_2026:READ");

        // Act
        await Rename("PAIE_2026");

        // Assert
        UserCodes().Should().Equal(Coded("SAGE:PAIE_2026:READ"));
    }

    [Fact]
    public async Task Save_SameName_LeavesRightsUntouched()
    {
        // Arrange
        User("SAGE:PAIE_2025:READ");
        var before = UserCodes();

        // Act
        await Rename("PAIE_2025");

        // Assert
        UserCodes().Should().Equal(before);
    }

    [Fact]
    public async Task Save_RenamedBase_UserKeepsEffectiveReadOnIt()
    {
        // Arrange
        User("SAGE:PAIE_2025:READ");

        // Act
        await Rename("PAIE_2026");

        // Assert
        var rights = await SoftTime.Tests.Http.TestRights.Service(Uow).GetAsync(2, fresh: true);
        rights.Has(Coded("SAGE:PAIE_2026:READ")).Should().BeTrue();
        rights.Has(Coded("SAGE:PAIE_2025:READ")).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_RemovesOrphanCodesAndKeepsOtherBases()
    {
        // Arrange
        Role("SAGE:PAIE_2025:READ", "SAGE:PAIE_2025:CREATE", "SAGE:AUTRE:READ");
        User("SAGE:PAIE_2025:READ", "PROC:SHIFTS");
        Uow.ResetSaveChangesCount();

        // Act
        await Delete();

        // Assert
        RoleCodes().Should().Equal(Coded("SAGE:AUTRE:READ"));
        UserCodes().Should().Equal("PROC:SHIFTS");
        Uow.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Delete_OwnerLeftWithoutRows_GetsEmptyMarkerNotGateDefaults()
    {
        // Arrange
        // Sans marqueur, le rôle (porte Bases) retomberait sur la lecture de TOUTES les bases.
        Uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 5, IdDroit = 1 });
        Role("SAGE:PAIE_2025:READ");
        User("SAGE:PAIE_2025:READ");

        // Act
        await Delete();

        // Assert
        RoleCodes().Should().Equal("__SOFTTIME_EMPTY_ROLE_PERMISSIONS__");
        // Compte : ses lignes sont des écarts (migration 006) ; sans ligne, il a exactement les droits de son rôle.
        UserCodes().Should().BeEmpty();
        var rights = await SoftTime.Tests.Http.TestRights.Service(Uow).GetAsync(2, fresh: true);
        rights.Has(Coded("SAGE:AUTRE:READ")).Should().BeFalse();
    }
}
