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
using SoftTime.Infrastructure.Services;

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
    public void Defaults_AreDefinedInCode()
    {
        var o = Lus();

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
    public void Section_OverridesEveryKey()
    {
        var o = Lus(
            ("Closing:Enabled", "false"), ("Closing:ShowNames", "false"),
            ("Closing:DuplicateToleranceSeconds", "60"), ("Closing:IgnorerSemainesValidees", "false"),
            ("Closing:MaxPeriodDays", "31"), ("Closing:CriticalRatioBlocking", "0.05"),
            ("Closing:Gravites:QuasiDoublon", "Importante"), ("Closing:Gravites:pointagemanquant", "mineure"),
            ("Closing:Poids:Critique", "10"));
        var regles = o.ReglesParDefaut();

        o.Erreurs().Should().BeEmpty();
        o.Enabled.Should().BeFalse();
        o.ShowNames.Should().BeFalse();
        o.MaxPeriodDays.Should().Be(31);
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
    public void InvalidValue_IsRefusedWithClearMessageWithoutTheValue(string cle, string valeur, string attendu)
    {
        var o = Lus((cle, valeur));
        var resultat = new ClosingOptionsValidator().Validate(null, o);

        resultat.Failed.Should().BeTrue();
        var message = resultat.Failures!.Should().ContainSingle().Which;
        message.Should().Contain("Réglage de clôture invalide");
        message.Should().Contain(attendu);
        message.Replace(attendu, string.Empty).Should().NotContain(valeur);
    }

    [Fact]
    public void AllWeightsZero_IsRefused()
    {
        var o = Lus(("Closing:Poids:Mineure", "0"), ("Closing:Poids:Importante", "0"), ("Closing:Poids:Critique", "0"));

        o.Erreurs().Should().Contain(e => e.Contains("poids positif"));
    }

    [Fact]
    public void Validator_IsRegisteredAndBlocksResolution()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Closing:CriticalRatioBlocking"] = "2" })
            .Build());
        services.AddApplication();
        using var provider = services.BuildServiceProvider();

        var erreur = FluentActions.Invoking(() => provider.GetRequiredService<IOptions<ClosingOptions>>().Value).Should().ThrowExactly<OptionsValidationException>().Which;
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
    public void Status_FromCriticalRatio(int critiques, int jours, string attendu)
    {
        (ClosingStatusRules.Statut(critiques, jours, 0.02m)?.ToString()).Should().Be(attendu);
    }

    [Fact]
    public void Status_NoControlledDay_IsNull_AndOnlyCriticalsCount()
    {
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
        ClosingStatusRules.Statut(1, 1000, 0m).Should().Be(ClosingStatus.Bloquant);
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
    public async Task ConfiguredSeverity_ChangesListScoreAndStatus()
    {
        var request = new ClosingAnalysisRequest(Lundi, Lundi);

        var parDefaut = await Service(new ClosingOptions()).AnalyserAsync(request);
        var reglages = Lus(("Closing:Gravites:PointageManquant", "Mineure"));
        var modifie = await Service(reglages).AnalyserAsync(request);

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
    public async Task ConfiguredMaxPeriod_IsEnforced()
    {
        var service = Service(Lus(("Closing:MaxPeriodDays", "7")));

        await FluentActions.Awaiting(() =>
            service.AnalyserAsync(new ClosingAnalysisRequest(Lundi, Lundi.AddDays(7)))).Should().ThrowExactlyAsync<DomainException>();
        await service.AnalyserAsync(new ClosingAnalysisRequest(Lundi, Lundi.AddDays(6)));
    }

    [Fact]
    public async Task ExplicitRuleOptions_InRequest_TakePrecedenceOverSettings()
    {
        var service = Service(Lus(("Closing:Gravites:PointageManquant", "Mineure")));

        var resultat = await service.AnalyserAsync(new ClosingAnalysisRequest(Lundi, Lundi, Options: ClosingRulesOptions.Default));

        resultat.Anomalies.Items.Should().ContainSingle().Which.Gravite.Should().Be("Critique");
    }
}
