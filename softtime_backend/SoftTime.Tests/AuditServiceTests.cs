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
        types.Should().NotBeEmpty();
        types.Should().Contain(t => t.Value == "nvarchar");
    }

    [Fact]
    public async Task SaveConfigAsync_Create_WithValidType_NormalizesAndAddsEntity()
    {
        var dto = new AuditConfigDto(0, "T_EMPLOYEE", "NOM", "  VARCHAR  ", true, "SOFTTIME_SUITE");

        await _service.SaveConfigAsync(dto);

        var repo = _uow.Get<SuiviConfiguration>();
        repo.Items.Should().ContainSingle();
        var added = repo.Items[0];
        added.Tables.Should().Be("T_EMPLOYEE");
        added.Colonnes.Should().Be("NOM");
        added.Types.Should().Be("varchar");
        added.Etats.Should().BeTrue();
        added.Bases.Should().Be("SOFTTIME_SUITE");
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

        await FluentActions.Awaiting(() => _service.SaveConfigAsync(dto)).Should().ThrowExactlyAsync<DomainException>();

        var repo = _uow.Get<SuiviConfiguration>();
        repo.Items.Should().BeEmpty();
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

        existing.Colonnes.Should().Be("COMMENTAIRE_RENOMME");
        existing.Types.Should().Be("legacy_exotic_type");
        existing.Etats.Should().BeFalse();
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

        existing.Types.Should().Be("int");
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

        await FluentActions.Awaiting(() => _service.SaveConfigAsync(dto)).Should().ThrowExactlyAsync<DomainException>();

        existing.Types.Should().Be("varchar");
    }

    [Fact]
    public async Task SaveConfigAsync_Update_WhenNotFound_ThrowsNotFoundException()
    {
        var dto = new AuditConfigDto(999, "T_TEST", "COL1", "int", true, "SOFTTIME_SUITE");

        await FluentActions.Awaiting(() => _service.SaveConfigAsync(dto)).Should().ThrowExactlyAsync<NotFoundException>();
    }

    #region In-Memory Test Doubles

    #endregion
}
