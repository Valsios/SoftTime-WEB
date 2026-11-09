using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services.DataSources;

namespace SoftTime.Tests;

/// <summary>
/// Modification d'une base SAGE côté serveur (l'autorité) : le droit se vérifie sur le nom STOCKÉ de la ligne, pas sur le nom envoyé ;
/// le renommage est permis à qui a le droit sur le nom actuel ; un doublon serveur + nom est un conflit (409).
/// Mêmes doublures que DatabaseConfigurationSecurityTests.
/// </summary>
public class SageDatabaseUpdateRightsTests
{
    private const string Base = "BASE1";

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
    private readonly AuthServices _auth;

    public SageDatabaseUpdateRightsTests()
    {
        _auth = new AuthServices(_uow, ServiceMocks.TokenService().Object, _mapper, ServiceMocks.EmailService().Object, new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), new PasswordHasher<Responsable>());
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 5, NomRole = "Gestionnaire" });
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 7, NomBd = Base, Serveur = "srv", TypeBase = "STANDARD" });
        _uow.Get<BddPointeuse>().Items.Add(new BddPointeuse { Id = 8, NomBd = "PTG", Serveur = "srv" });
    }

    private CatalogServices CatalogFor(decimal userId, decimal roleId, params int[] rights)
    {
        if (!_uow.Get<Responsable>().Items.Any(u => u.IdResponsable == userId))
            _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = userId, IdRole = roleId, Login = $"u{userId}" });
        var authorizer = new DatabaseConfigurationAuthorizer(new TestCurrentUser(userId, roleId, rights), _auth);
        return new CatalogServices(_uow, _mapper, null!, null!, null!, authorizer, SoftTime.Tests.Http.TestRights.Service(_uow));
    }

    private void GrantUser(decimal userId, params string[] codes)
    {
        foreach (var c in codes)
            _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = userId, PermissionCode = c, IsGranted = true });
    }

    private BddSage Row(int id) => _uow.Get<BddSage>().Items.Single(s => s.Id == id);

    [Fact]
    public async Task SaveSageAsync_PermissionOnCurrentName_UpdatesServer()
    {
        // Arrange
        GrantUser(40, $"SAGE:{Base}:UPDATE");
        var svc = CatalogFor(40, 5, Rights.Databases);

        // Act
        await svc.Sage.SaveSageAsync(new SageDbDto(7, "srv2", null, null, true, Base));

        // Assert
        Row(7).Serveur.Should().Be("srv2");
        Row(7).NomBd.Should().Be(Base);
    }

    [Fact]
    public async Task SaveSageAsync_PermissionOnlyOnNewName_IsRefusedAndRowUnchanged()
    {
        // Arrange : le droit ne porte que sur le nom demandé, pas sur le nom stocké.
        GrantUser(41, "SAGE:NOUVEAU:UPDATE");
        var svc = CatalogFor(41, 5, Rights.Databases);

        // Act
        var act = () => svc.Sage.SaveSageAsync(new SageDbDto(7, "srv", null, null, true, "NOUVEAU"));

        // Assert
        await act.Should().ThrowExactlyAsync<ForbiddenException>();
        Row(7).NomBd.Should().Be(Base);
    }

    [Fact]
    public async Task SaveSageAsync_RenameWithPermissionOnCurrentName_IsAllowed()
    {
        // Arrange
        GrantUser(42, $"SAGE:{Base}:UPDATE");
        var svc = CatalogFor(42, 5, Rights.Databases);

        // Act
        var result = await svc.Sage.SaveSageAsync(new SageDbDto(7, "srv", null, null, true, "RENOMMEE"));

        // Assert
        result.NomBd.Should().Be("RENOMMEE");
        Row(7).NomBd.Should().Be("RENOMMEE");
        _uow.Get<BddSage>().Items.Should().ContainSingle();
    }

    [Fact]
    public async Task SaveSageAsync_RenameToExistingServerAndName_ThrowsConflict()
    {
        // Arrange
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 10, NomBd = "AUTRE", Serveur = "srv", TypeBase = "STANDARD" });
        var svc = CatalogFor(1, 1);

        // Act
        var act = () => svc.Sage.SaveSageAsync(new SageDbDto(7, "srv", null, null, true, "AUTRE"));

        // Assert
        await act.Should().ThrowExactlyAsync<ConflictException>();
        Row(7).NomBd.Should().Be(Base);
    }

    [Fact]
    public async Task SaveSageAsync_SameNameOnAnotherServer_IsNotADuplicate()
    {
        // Arrange
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 10, NomBd = "AUTRE", Serveur = "srv-b", TypeBase = "STANDARD" });
        var svc = CatalogFor(1, 1);

        // Act
        await svc.Sage.SaveSageAsync(new SageDbDto(7, "srv", null, null, true, "AUTRE"));

        // Assert
        Row(7).NomBd.Should().Be("AUTRE");
    }
}
