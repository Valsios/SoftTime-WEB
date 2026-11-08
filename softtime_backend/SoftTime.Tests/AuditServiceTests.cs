using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

public class AuditServiceTests
{
    private readonly InMemoryUnitOfWork _uow;
    private readonly AuditService _service;

    public AuditServiceTests()
    {
        _uow = new InMemoryUnitOfWork();
        _service = new AuditService(_uow);
    }

    [Fact]
    public void ListAllowedTypes_ReturnsNonEmptyOptions()
    {
        var types = _service.ListAllowedTypes();
        Assert.NotEmpty(types);
        Assert.Contains(types, t => t.Value == "nvarchar");
    }

    [Fact]
    public async Task SaveConfigAsync_Create_WithValidType_NormalizesAndAddsEntity()
    {
        var dto = new AuditConfigDto(0, "T_EMPLOYEE", "NOM", "  VARCHAR  ", true, "SOFTTIME_SUITE");

        await _service.SaveConfigAsync(dto);

        var repo = _uow.Get<SuiviConfiguration>();
        Assert.Single(repo.Items);
        var added = repo.Items[0];
        Assert.Equal("T_EMPLOYEE", added.Tables);
        Assert.Equal("NOM", added.Colonnes);
        Assert.Equal("varchar", added.Types);
        Assert.True(added.Etats);
        Assert.Equal("SOFTTIME_SUITE", added.Bases);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("text")]
    [InlineData("image")]
    [InlineData("not_a_sql_type")]
    public async Task SaveConfigAsync_Create_WithInvalidType_ThrowsDomainException(string? invalidType)
    {
        var dto = new AuditConfigDto(0, "T_EMPLOYEE", "NOM", invalidType, true, "SOFTTIME_SUITE");

        await Assert.ThrowsAsync<DomainException>(() => _service.SaveConfigAsync(dto));

        var repo = _uow.Get<SuiviConfiguration>();
        Assert.Empty(repo.Items);
    }

    [Fact]
    public async Task SaveConfigAsync_Update_WithUnchangedLegacyType_DoesNotThrowAndPreservesValue()
    {
        var repo = _uow.Get<SuiviConfiguration>();
        var existing = new SuiviConfiguration
        {
            Id = 42,
            Tables = "T_LOG",
            Colonnes = "COMMENTAIRE",
            Types = "legacy_exotic_type",
            Etats = true,
            Bases = "SOFTTIME_SUITE"
        };
        repo.Items.Add(existing);

        var dto = new AuditConfigDto(42, "T_LOG", "COMMENTAIRE_RENOMME", "legacy_exotic_type", false, "SOFTTIME_SUITE");

        await _service.SaveConfigAsync(dto);

        Assert.Equal("COMMENTAIRE_RENOMME", existing.Colonnes);
        Assert.Equal("legacy_exotic_type", existing.Types);
        Assert.False(existing.Etats);
    }

    [Fact]
    public async Task SaveConfigAsync_Update_WithModifiedValidType_ValidatesAndNormalizes()
    {
        var repo = _uow.Get<SuiviConfiguration>();
        var existing = new SuiviConfiguration
        {
            Id = 10,
            Tables = "T_TEST",
            Colonnes = "COL1",
            Types = "varchar",
            Etats = true,
            Bases = "SOFTTIME_SUITE"
        };
        repo.Items.Add(existing);

        var dto = new AuditConfigDto(10, "T_TEST", "COL1", "  INT  ", true, "SOFTTIME_SUITE");

        await _service.SaveConfigAsync(dto);

        Assert.Equal("int", existing.Types);
    }

    [Fact]
    public async Task SaveConfigAsync_Update_WithModifiedInvalidType_ThrowsDomainExceptionAndLeavesOriginal()
    {
        var repo = _uow.Get<SuiviConfiguration>();
        var existing = new SuiviConfiguration
        {
            Id = 10,
            Tables = "T_TEST",
            Colonnes = "COL1",
            Types = "varchar",
            Etats = true,
            Bases = "SOFTTIME_SUITE"
        };
        repo.Items.Add(existing);

        var dto = new AuditConfigDto(10, "T_TEST", "COL1", "invalid_type", true, "SOFTTIME_SUITE");

        await Assert.ThrowsAsync<DomainException>(() => _service.SaveConfigAsync(dto));

        Assert.Equal("varchar", existing.Types);
    }

    [Fact]
    public async Task SaveConfigAsync_Update_WhenNotFound_ThrowsNotFoundException()
    {
        var dto = new AuditConfigDto(999, "T_TEST", "COL1", "int", true, "SOFTTIME_SUITE");

        await Assert.ThrowsAsync<NotFoundException>(() => _service.SaveConfigAsync(dto));
    }

    #region In-Memory Test Doubles

    #endregion
}
