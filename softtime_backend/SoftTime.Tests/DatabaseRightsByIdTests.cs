using Microsoft.Extensions.Logging.Abstractions;

using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services.Permissions;
using SoftTime.Tests.Http;

using static SoftTime.Application.Security.EffectiveRightsCalculator;

namespace SoftTime.Tests;

/// <summary>
/// Lot 4 : droits de base par identifiant (un renommage ne fait rien perdre), joker « all » pour toutes les bases
/// présentes et futures, et règle « une action d'écran exige la page de son écran ».
/// </summary>
public class DatabaseRightsByIdTests
{
    private static readonly DatabaseCatalog Paie = new(new[] { new DatabaseRef(12, "PAIE") }, Array.Empty<DatabaseRef>(), new[] { 12 });

    private static EffectiveRights Compute(string[] role, PermissionRow[]? user = null, DatabaseCatalog? dbs = null)
        => ComputeEffectiveRights(
            new RoleDefinition(5, true, role.Select(c => new PermissionRow(c)).ToList()),
            user ?? Array.Empty<PermissionRow>(),
            dbs ?? Paie);

    [Fact]
    public void CanUseDatabase_RenamedDatabase_KeepsItsRights()
    {
        // Arrange : même identifiant 12, nouveau nom.
        var renamed = new DatabaseCatalog(new[] { new DatabaseRef(12, "PAIE_2026") }, Array.Empty<DatabaseRef>(), new[] { 12 });

        // Act
        var sut = Compute(new[] { "sage.12.read" }, dbs: renamed);

        // Assert
        sut.CanUseDatabase(DatabaseKind.Sage, "PAIE_2026").Should().BeTrue();
        sut.CanUseDatabase(DatabaseKind.Sage, "PAIE").Should().BeFalse();
    }

    [Fact]
    public void CanUseDatabase_AllJoker_CoversADatabaseDeclaredLater()
    {
        // Arrange
        var later = new DatabaseCatalog(new[] { new DatabaseRef(12, "PAIE"), new DatabaseRef(40, "NOUVELLE") }, Array.Empty<DatabaseRef>(), new[] { 12, 40 });

        // Act
        var sut = Compute(new[] { "sage.all.read" }, dbs: later);

        // Assert
        sut.CanUseDatabase(DatabaseKind.Sage, "NOUVELLE").Should().BeTrue();
        sut.CanConfigureDatabase(DatabaseKind.Sage, DatabaseConfigAction.Update, "NOUVELLE").Should().BeFalse();
    }

    [Fact]
    public void Compute_ActionWithoutItsPage_IsIgnoredAndReported()
    {
        // Act : le compte retire la page, le rôle garde l'action.
        var sut = Compute(new[] { "categories.read", "categories.delete" }, new[] { new PermissionRow("categories.read", false) });

        // Assert
        sut.Has("categories.delete").Should().BeFalse();
        sut.Inconsistencies.Should().ContainSingle(i => i.Code == "ACTION_WITHOUT_PAGE");
    }

    [Fact]
    public void Compute_DatabaseUpdateWithoutRead_IsKept()
    {
        // Act : configurer une connexion n'exige pas d'en lire les données.
        var sut = Compute(new[] { "sage.12.update" });

        // Assert
        sut.Has("sage.12.update").Should().BeTrue();
        sut.Inconsistencies.Should().NotContain(i => i.Code == "ACTION_WITHOUT_PAGE");
    }

    [Fact]
    public void Normalize_ActionWithoutItsPage_IsRefused()
    {
        // Act
        Action act = () => PermissionCodeNormalizer.Normalize(new[] { "categories.delete", "shifts.read" });

        // Assert
        act.Should().ThrowExactly<DomainException>().Which.Message.Should().Contain("categories.delete (exige categories.read)");
        PermissionCodeNormalizer.Normalize(new[] { "categories.delete", "Categories.Read" }).Should().HaveCount(2);
    }

    [Fact]
    public async Task Migration_RoleActionWithoutPage_ReceivesThePage()
    {
        // Arrange
        var uow = new InMemoryUnitOfWork();
        uow.Get<Role>().Items.Add(new Role { IdRole = 5, NomRole = "Orphelin" });
        uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 5, PermissionCode = "holidays.delete", IsGranted = true });

        // Act
        await new RightsStorageMigration(uow, NullLogger<RightsStorageMigration>.Instance).RunAsync();

        // Assert
        uow.Get<RolePermission>().Items.Select(p => p.PermissionCode).Should().BeEquivalentTo("holidays.delete", "holidays.read");
    }

    [Fact]
    public async Task DeleteSageDatabase_RemovesItsNamedRightsButKeepsJokers()
    {
        // Arrange
        await using var factory = new ApiFactory();
        var rows = factory.Store.Get<RolePermission>().Items;
        rows.Add(new RolePermission { Id = 9001, IdRole = 91, PermissionCode = "sage.3035.read", IsGranted = true });
        rows.Add(new RolePermission { Id = 9002, IdRole = 91, PermissionCode = "sage.all.read", IsGranted = true });
        var http = factory.CreateClient();

        // Act
        var response = await http.SendAsync(factory.Request("admin", HttpMethod.Delete, "/api/v1/sage-databases/3035"));

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
        rows.Select(p => p.PermissionCode).Should().NotContain("sage.3035.read").And.Contain("sage.all.read");
    }
}
