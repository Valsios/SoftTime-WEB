using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using SoftTime.Application;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Services.Closing;
using SoftTime.Infrastructure.Services.Employees;
using SoftTime.Infrastructure.Services.RequestContext;

namespace SoftTime.Tests;

/// <summary>Réglages « Closing » (lecture, validation, effet sur l'analyse) et statut de clôture.</summary>
public class ClosingSettingsTests
{
    private static ClosingOptions Lus(params (string Cle, string Valeur)[] valeurs)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(valeurs.Select(v => new KeyValuePair<string, string?>(v.Cle, v.Valeur)))
            .Build();
        var options = new ClosingOptions();
        new ClosingOptionsSetup(configuration).Configure(options);
        return options;
    }

    // ---------- Valeurs par défaut ----------

    [Fact]
    public void ClosingOptions_NoSection_UsesDefaultsDefinedInCode()
    {
        // Act
        var o = Lus();

        // Assert
        o.Enabled.Should().BeTrue();
        o.ShowNames.Should().BeTrue();
        o.DuplicateToleranceSeconds.Should().Be(30);
        o.ExclureSansPlanningSurPeriode.Should().BeTrue();
        o.DoublonsMonoGravite.Should().Be(ClosingSeverity.Mineure);
        o.IgnorerSemainesValidees.Should().BeTrue();
        o.MaxPeriodDays.Should().Be(366);
        o.CriticalRatioBlocking.Should().Be(0.05m);
        o.Gravites.Should().BeEmpty();
        new[] { o.Poids[ClosingSeverity.Information], o.Poids[ClosingSeverity.Mineure], o.Poids[ClosingSeverity.Importante], o.Poids[ClosingSeverity.Critique] }.Should().Equal(new[] { 0, 1, 3, 5 });
        o.Erreurs().Should().BeEmpty();
        var regles = o.ReglesParDefaut();
        regles.Gravites.Should().BeEmpty();
        regles.Should().Be(ClosingRulesOptions.Default with { Gravites = regles.Gravites });
    }

    [Fact]
    public void ReglesParDefaut_ConfiguredSection_OverridesEveryKey()
    {
        // Arrange
        var sut = Lus(
            ("Closing:Enabled", "false"), ("Closing:ShowNames", "false"),
            ("Closing:DuplicateToleranceSeconds", "60"), ("Closing:IgnorerSemainesValidees", "false"),
            ("Closing:MaxPeriodDays", "31"), ("Closing:CriticalRatioBlocking", "0.05"),
            ("Closing:Gravites:QuasiDoublon", "Importante"), ("Closing:Gravites:pointagemanquant", "mineure"),
            ("Closing:Poids:Critique", "10"));

        // Act
        var regles = sut.ReglesParDefaut();

        // Assert
        sut.Erreurs().Should().BeEmpty();
        sut.Enabled.Should().BeFalse();
        sut.ShowNames.Should().BeFalse();
        sut.MaxPeriodDays.Should().Be(31);
        regles.DuplicateToleranceSeconds.Should().Be(60);
        regles.IgnorerSemainesValidees.Should().BeFalse();
        regles.CriticalRatioBlocking.Should().Be(0.05m);
        regles.Gravite(ClosingAnomalyType.QuasiDoublon).Should().Be(ClosingSeverity.Importante);
        regles.Gravite(ClosingAnomalyType.PointageManquant).Should().Be(ClosingSeverity.Mineure);
        regles.Gravite(ClosingAnomalyType.PointageIncomplet).Should().Be(ClosingSeverity.Critique);
        regles.PoidsMaximum.Should().Be(10);
    }

    // ---------- Valeurs invalides ----------

    [Theory]
    [InlineData("Closing:DuplicateToleranceSeconds", "-1", "DuplicateToleranceSeconds")]
    [InlineData("Closing:DuplicateToleranceSeconds", "abc", "DuplicateToleranceSeconds")]
    [InlineData("Closing:MaxPeriodDays", "0", "MaxPeriodDays")]
    [InlineData("Closing:MaxPeriodDays", "400", "MaxPeriodDays")]
    [InlineData("Closing:CriticalRatioBlocking", "1.5", "CriticalRatioBlocking")]
    [InlineData("Closing:CriticalRatioBlocking", "-0.1", "CriticalRatioBlocking")]
    [InlineData("Closing:CriticalRatioBlocking", "0,02", "CriticalRatioBlocking")]
    [InlineData("Closing:Enabled", "oui", "Enabled")]
    [InlineData("Closing:Gravites:Inconnu", "Critique", "Gravites:Inconnu")]
    [InlineData("Closing:Gravites:QuasiDoublon", "Grave", "Gravites:QuasiDoublon")]
    [InlineData("Closing:Gravites:QuasiDoublon", "5", "Gravites:QuasiDoublon")]
    [InlineData("Closing:Poids:Mineure", "-2", "Poids:Mineure")]
    [InlineData("Closing:Poids:Enorme", "2", "Poids:Enorme")]
    public void Validate_InvalidValue_IsRefusedWithClearMessageWithoutTheValue(string cle, string valeur, string attendu)
    {
        // Arrange
        var o = Lus((cle, valeur));

        // Act
        var resultat = new ClosingOptionsValidator().Validate(null, o);

        // Assert
        resultat.Failed.Should().BeTrue();
        var message = resultat.Failures!.Should().ContainSingle().Which;
        message.Should().Contain("Réglage de clôture invalide");
        message.Should().Contain(attendu);
        message.Replace(attendu, string.Empty).Should().NotContain(valeur);
    }

    [Fact]
    public void Erreurs_AllWeightsZero_IsRefused()
    {
        // Arrange
        var sut = Lus(("Closing:Poids:Mineure", "0"), ("Closing:Poids:Importante", "0"), ("Closing:Poids:Critique", "0"));

        // Act
        var result = sut.Erreurs();

        // Assert
        result.Should().Contain(e => e.Contains("poids positif"));
    }

    [Fact]
    public void ClosingOptionsValidator_InvalidOptions_BlocksResolution()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Closing:CriticalRatioBlocking"] = "2" })
            .Build());
        services.AddApplication();
        using var sut = services.BuildServiceProvider();

        // Act
        var act = () => sut.GetRequiredService<IOptions<ClosingOptions>>().Value;

        // Assert
        var erreur = act.Should().ThrowExactly<OptionsValidationException>().Which;
        erreur.Message.Should().Contain("CriticalRatioBlocking");
    }

    // ---------- Statut ----------

    [Theory]
    [InlineData(0, 100, "Pret")]
    [InlineData(1, 100, "ACorriger")]
    [InlineData(2, 100, "Bloquant")]
    [InlineData(3, 100, "Bloquant")]
    [InlineData(1, 51, "ACorriger")]
    [InlineData(1, 50, "Bloquant")]
    public void Statut_CriticalRatio_ReturnsExpectedStatus(int critiques, int jours, string attendu)
    {
        (ClosingStatusRules.Statut(critiques, jours, 0.02m)?.ToString()).Should().Be(attendu);
    }

    [Fact]
    public void Status_NoControlledDay_IsNull_AndOnlyCriticalsCount()
    {
        // Act & Assert
        ClosingStatusRules.Statut(0, 0, 0.02m).Should().BeNull();
        var mineures = Enumerable.Range(0, 50)
            .Select(_ => new ClosingAnomaly(ClosingAnomalyType.QuasiDoublon, ClosingSeverity.Mineure, "1", DateTime.Today, "x"))
            .ToList();
        ClosingStatusRules.Statut(mineures, 10, 0.02m).Should().Be(ClosingStatus.Pret);
        ClosingStatusRules.Libelle(ClosingStatus.ACorriger).Should().Be("À corriger");
    }

    [Fact]
    public void Status_ZeroRatio_AnyCriticalBlocks()
    {
        // Act
        var result = ClosingStatusRules.Statut(1, 1000, 0m);

        // Assert
        result.Should().Be(ClosingStatus.Bloquant);
    }

    // ---------- Effet des réglages sur l'analyse ----------

    private static readonly DateTime Lundi = new(2025, 11, 17);

    private sealed class HorlogeFixe : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(new DateTime(2025, 12, 1, 10, 0, 0), TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static ClosingService Service(ClosingOptions reglages)
    {
        var uow = new InMemoryUnitOfWork();
        uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "SAGE_TEST" });
        uow.Get<ParametrePointeuse>().Items.Add(new ParametrePointeuse { IdPoint = 1, MultiPoint = false });
        uow.Get<CardPaie>().Items.Add(new CardPaie { Id = 1, SageMatricule = "001", SageBdd = "SAGE_TEST", PointeuseBdd = "PTE_TEST" });
        uow.Get<PlanningSalarie>().Items.Add(new PlanningSalarie
        {
            Id = 1, IdSal = 1, DateP = Lundi, Ha = Lundi.AddHours(8), Hd = Lundi.AddHours(17), OffP = false, BddSage = "SAGE_TEST"
        });
        var tenant = new TenantConnectionService(uow, new FakeCompanyContext(), new FakeCurrentUser(),
            SoftTime.Tests.Http.TestRights.Service(uow), NullLogger<TenantConnectionService>.Instance);
        return new ClosingService(uow, tenant, new SageLeaveCalendar(tenant, new FakeExternalSourceService()), new HorlogeFixe(), Options.Create(reglages));
    }

    [Fact]
    public async Task AnalyserAsync_ConfiguredSeverity_ChangesListScoreAndStatus()
    {
        // Arrange
        var request = new ClosingAnalysisRequest(Lundi, Lundi);
        var parDefaut = await Service(new ClosingOptions()).AnalyserAsync(request);
        var reglages = Lus(("Closing:Gravites:PointageManquant", "Mineure"));

        // Act
        var modifie = await Service(reglages).AnalyserAsync(request);

        // Assert
        // Un jour planifié sans pointage : Critique (5) → score 100 − 100 × 5 / 5 = 0, statut Bloquant.
        parDefaut.Anomalies.Items.Should().ContainSingle().Which.Gravite.Should().Be("Critique");
        parDefaut.Score.Should().Be(0);
        parDefaut.Statut.Should().Be("Bloquant");
        // Gravité ramenée à Mineure (1) : 100 − 100 × 1 / 5 = 80, aucune critique → Prêt.
        var anomalie = modifie.Anomalies.Items.Should().ContainSingle().Which;
        anomalie.Gravite.Should().Be("Mineure");
        anomalie.Poids.Should().Be(1);
        modifie.Score.Should().Be(80);
        modifie.Statut.Should().Be("Pret");
        modifie.ParGravite.Single(g => g.Cle == "Mineure").Nombre.Should().Be(1);
    }

    [Fact]
    public async Task AnalyserAsync_PeriodAboveConfiguredMaximum_IsRefused()
    {
        // Arrange
        var sut = Service(Lus(("Closing:MaxPeriodDays", "7")));

        // Act
        Func<Task> act = () =>
            sut.AnalyserAsync(new ClosingAnalysisRequest(Lundi, Lundi.AddDays(7)));

        // Assert
        await act.Should().ThrowExactlyAsync<DomainException>();
        await sut.AnalyserAsync(new ClosingAnalysisRequest(Lundi, Lundi.AddDays(6)));
    }

    [Fact]
    public async Task ExplicitRuleOptions_InRequest_TakePrecedenceOverSettings()
    {
        // Arrange
        var sut = Service(Lus(("Closing:Gravites:PointageManquant", "Mineure")));

        // Act
        var resultat = await sut.AnalyserAsync(new ClosingAnalysisRequest(Lundi, Lundi, Options: ClosingRulesOptions.Default));

        // Assert
        resultat.Anomalies.Items.Should().ContainSingle().Which.Gravite.Should().Be("Critique");
    }
}
