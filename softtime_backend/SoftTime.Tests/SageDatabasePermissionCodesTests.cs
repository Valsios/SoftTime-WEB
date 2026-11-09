using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services.DataSources;

namespace SoftTime.Tests;

/// <summary>Lot 6 : les codes SAGE:&lt;nom&gt;:… suivent le renommage d'une base et disparaissent à sa suppression.</summary>
public class SageDatabasePermissionCodesTests
{
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
    private readonly CatalogServices _sut;

    public SageDatabasePermissionCodesTests()
    {
        var auth = new AuthServices(_uow, ServiceMocks.TokenService().Object, _mapper, ServiceMocks.EmailService().Object,
            new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), new PasswordHasher<Responsable>());
        _sut = new CatalogServices(_uow, _mapper, null!, null!, SoftTime.Tests.Http.NullObject.Create<IExternalDiscoveryService>(),
            new DatabaseConfigurationAuthorizer(new TestCurrentUser(1, 1), auth), SoftTime.Tests.Http.TestRights.Service(_uow));

        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 5, NomRole = "Gestionnaire" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 2, IdRole = 5, Login = "gestionnaire" });
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 7, NomBd = "PAIE_2025", Serveur = "srv", TLogin = "sa", TMdp = "FAKE", TypeAuth = true });
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 8, NomBd = "AUTRE", Serveur = "srv", TLogin = "sa", TMdp = "FAKE", TypeAuth = true });
    }

    private void Role(params string[] codes)
    {
        foreach (var code in codes)
            _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 5, PermissionCode = code, IsGranted = true });
    }

    private void User(params string[] codes)
    {
        foreach (var code in codes)
            _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 2, PermissionCode = code, IsGranted = true });
    }

    private List<string> RoleCodes() => _uow.Get<RolePermission>().Items.Where(p => p.IdRole == 5).Select(p => p.PermissionCode).ToList();

    private List<string> UserCodes() => _uow.Get<UserPermission>().Items.Where(p => p.IdResponsable == 2).Select(p => p.PermissionCode).ToList();

    private Task Rename(string newName)
        => _sut.Sage.SaveSageAsync(new SageDbDto(7, "srv", "sa", null, true, newName));

    [Fact]
    public async Task SaveSageAsync_Rename_RightsFollowTheNewNameInOneSave()
    {
        // Arrange
        Role("PROC:SHIFTS", "SAGE:PAIE_2025:READ", "SAGE:AUTRE:READ");
        User("SAGE:PAIE_2025:READ", "SAGE:PAIE_2025:UPDATE", "SAGE:paie_2025:delete");
        _uow.ResetSaveChangesCount();

        // Act
        await Rename("PAIE_2026");

        // Assert
        RoleCodes().Should().BeEquivalentTo("PROC:SHIFTS", "SAGE:PAIE_2026:READ", "SAGE:AUTRE:READ");
        UserCodes().Should().BeEquivalentTo("SAGE:PAIE_2026:READ", "SAGE:PAIE_2026:UPDATE", "SAGE:PAIE_2026:DELETE");
        _uow.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task SaveSageAsync_RenameWhenNewCodeAlreadyHeld_KeepsASingleRow()
    {
        // Arrange
        User("SAGE:PAIE_2025:READ", "SAGE:PAIE_2026:READ");

        // Act
        await Rename("PAIE_2026");

        // Assert
        UserCodes().Should().Equal("SAGE:PAIE_2026:READ");
    }

    [Fact]
    public async Task SaveSageAsync_SameName_LeavesRightsUntouched()
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
    public async Task SaveSageAsync_RenamedBase_UserKeepsEffectiveReadOnIt()
    {
        // Arrange
        User("SAGE:PAIE_2025:READ");

        // Act
        await Rename("PAIE_2026");

        // Assert
        var rights = await SoftTime.Tests.Http.TestRights.Service(_uow).GetAsync(2, fresh: true);
        rights.Has("SAGE:PAIE_2026:READ").Should().BeTrue();
        rights.Has("SAGE:PAIE_2025:READ").Should().BeFalse();
    }

    [Fact]
    public async Task DeleteSageAsync_RemovesOrphanCodesAndKeepsOtherBases()
    {
        // Arrange
        Role("SAGE:PAIE_2025:READ", "SAGE:PAIE_2025:CREATE", "SAGE:AUTRE:READ");
        User("SAGE:PAIE_2025:READ", "PROC:SHIFTS");
        _uow.ResetSaveChangesCount();

        // Act
        await _sut.Sage.DeleteSageAsync(7);

        // Assert
        RoleCodes().Should().Equal("SAGE:AUTRE:READ");
        UserCodes().Should().Equal("PROC:SHIFTS");
        _uow.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task DeleteSageAsync_OwnerLeftWithoutRows_GetsEmptyMarkerNotGateDefaults()
    {
        // Arrange
        // Sans marqueur, le rôle (porte Bases) retomberait sur la lecture de TOUTES les bases.
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 5, IdDroit = 1 });
        Role("SAGE:PAIE_2025:READ");
        User("SAGE:PAIE_2025:READ");

        // Act
        await _sut.Sage.DeleteSageAsync(7);

        // Assert
        RoleCodes().Should().Equal("__SOFTTIME_EMPTY_ROLE_PERMISSIONS__");
        UserCodes().Should().Equal("__SOFTTIME_EMPTY_CUSTOM_PERMISSIONS__");
        var rights = await SoftTime.Tests.Http.TestRights.Service(_uow).GetAsync(2, fresh: true);
        rights.Has("SAGE:AUTRE:READ").Should().BeFalse();
    }
}
