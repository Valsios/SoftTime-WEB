using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using SoftTime.Application.Services;
using SoftTime.Infrastructure;
using SoftTime.Infrastructure.Integrations;
using SoftTime.Infrastructure.SchemaMigrations;

namespace SoftTime.Tests;

/// <summary>Liaison des sections « Database », « AuditJournal » et « Email » par AddInfrastructureOptions.</summary>
public class InfrastructureOptionsTests
{
    [Fact]
    public void SchemaMigration_EmptyConfiguration_IsDisabledWithSixtySecondLock()
    {
        // Act
        var sut = Bound<SchemaMigrationOptions>();

        // Assert
        sut.AutoMigrate.Should().BeFalse();
        sut.LockTimeoutSeconds.Should().Be(SchemaMigrationOptions.DefaultLockTimeoutSeconds);
    }

    [Fact]
    public void SchemaMigration_DatabaseSection_IsBound()
    {
        // Act
        var sut = Bound<SchemaMigrationOptions>(
            ("Database:AutoMigrate", "true"), ("Database:AutoMigrateLockTimeoutSeconds", "15"));

        // Assert
        sut.AutoMigrate.Should().BeTrue();
        sut.LockTimeoutSeconds.Should().Be(15);
    }

    [Fact]
    public void SchemaMigration_NegativeLockTimeout_FallsBackToDefault()
    {
        // Act
        var sut = Bound<SchemaMigrationOptions>(("Database:AutoMigrateLockTimeoutSeconds", "-1"));

        // Assert
        sut.LockTimeoutSeconds.Should().Be(SchemaMigrationOptions.DefaultLockTimeoutSeconds);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void AuditJournal_NonPositiveThreshold_FallsBackToDefault(string threshold)
    {
        // Act
        var sut = Bound<AuditJournalOptions>(("AuditJournal:DetailThreshold", threshold));

        // Assert
        sut.DetailThreshold.Should().Be(AuditJournalOptions.DefaultDetailThreshold);
    }

    [Fact]
    public void AuditJournal_Section_IsBound()
    {
        // Act
        var sut = Bound<AuditJournalOptions>(("AuditJournal:Enabled", "false"), ("AuditJournal:DetailThreshold", "50"));

        // Assert
        sut.Enabled.Should().BeFalse();
        sut.DetailThreshold.Should().Be(50);
    }

    [Fact]
    public void Email_Section_IsBoundIntoSettings()
    {
        // Act
        var sut = Bound<EmailOptions>(
            ("Email:Host", "smtp.exemple.com"), ("Email:Port", "25"), ("Email:EnableSsl", "false"),
            ("Email:FromAddress", "no-reply@exemple.com"), ("Email:FromName", "SoftTime")).ToSettings();

        // Assert
        sut.EstConfigure.Should().BeTrue();
        sut.PortEffectif.Should().Be(25);
        sut.EnableSsl.Should().BeFalse();
        sut.NomExpediteur.Should().Be("SoftTime");
    }

    [Fact]
    public void Email_EmptyConfiguration_IsNotConfigured()
    {
        // Act
        var sut = Bound<EmailOptions>().ToSettings();

        // Assert
        sut.EstConfigure.Should().BeFalse();
        sut.EnableSsl.Should().BeTrue();
    }

    private static TOptions Bound<TOptions>(params (string Key, string Value)[] values) where TOptions : class
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
        using var provider = new ServiceCollection().AddInfrastructureOptions(configuration).BuildServiceProvider();
        return provider.GetRequiredService<IOptions<TOptions>>().Value;
    }
}
