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

        Assert.True(o.Enabled);
        Assert.True(o.ShowNames);
        Assert.Equal(30, o.DuplicateToleranceSeconds);
        Assert.True(o.ExclureSansPlanningSurPeriode);
        Assert.Equal(ClosingSeverity.Mineure, o.DoublonsMonoGravite);
        Assert.True(o.IgnorerSemainesValidees);
        Assert.Equal(366, o.MaxPeriodDays);
        Assert.Equal(0.05m, o.CriticalRatioBlocking);
        Assert.Empty(o.Gravites);
        Assert.Equal(new[] { 0, 1, 3, 5 }, new[] { o.Poids[ClosingSeverity.Information], o.Poids[ClosingSeverity.Mineure], o.Poids[ClosingSeverity.Importante], o.Poids[ClosingSeverity.Critique] });
        Assert.Empty(o.Erreurs());
        var regles = o.ReglesParDefaut();
        Assert.Empty(regles.Gravites);
        Assert.Equal(ClosingRulesOptions.Default with { Gravites = regles.Gravites }, regles);
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

        Assert.Empty(o.Erreurs());
        Assert.False(o.Enabled);
        Assert.False(o.ShowNames);
        Assert.Equal(31, o.MaxPeriodDays);
        Assert.Equal(60, regles.DuplicateToleranceSeconds);
        Assert.False(regles.IgnorerSemainesValidees);
        Assert.Equal(0.05m, regles.CriticalRatioBlocking);
        Assert.Equal(ClosingSeverity.Importante, regles.Gravite(ClosingAnomalyType.QuasiDoublon));
        Assert.Equal(ClosingSeverity.Mineure, regles.Gravite(ClosingAnomalyType.PointageManquant));
        Assert.Equal(ClosingSeverity.Critique, regles.Gravite(ClosingAnomalyType.PointageIncomplet));
        Assert.Equal(10, regles.PoidsMaximum);
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

        Assert.True(resultat.Failed);
        var message = Assert.Single(resultat.Failures!);
        Assert.Contains("Réglage de clôture invalide", message);
        Assert.Contains(attendu, message);
        Assert.DoesNotContain(valeur, message.Replace(attendu, string.Empty));
    }

    [Fact]
    public void AllWeightsZero_IsRefused()
    {
        var o = Lus(("Closing:Poids:Mineure", "0"), ("Closing:Poids:Importante", "0"), ("Closing:Poids:Critique", "0"));

        Assert.Contains(o.Erreurs(), e => e.Contains("poids positif"));
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

        var erreur = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ClosingOptions>>().Value);
        Assert.Contains("CriticalRatioBlocking", erreur.Message);
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
        Assert.Equal(attendu, ClosingStatusRules.Statut(critiques, jours, 0.02m)?.ToString());
    }

    [Fact]
    public void Status_NoControlledDay_IsNull_AndOnlyCriticalsCount()
    {
        Assert.Null(ClosingStatusRules.Statut(0, 0, 0.02m));
        var mineures = Enumerable.Range(0, 50)
            .Select(_ => new ClosingAnomaly(ClosingAnomalyType.QuasiDoublon, ClosingSeverity.Mineure, "1", DateTime.Today, "x"))
            .ToList();
        Assert.Equal(ClosingStatus.Pret, ClosingStatusRules.Statut(mineures, 10, 0.02m));
        Assert.Equal("À corriger", ClosingStatusRules.Libelle(ClosingStatus.ACorriger));
    }

    [Fact]
    public void Status_ZeroRatio_AnyCriticalBlocks()
    {
        Assert.Equal(ClosingStatus.Bloquant, ClosingStatusRules.Statut(1, 1000, 0m));
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
        Assert.Equal("Critique", Assert.Single(parDefaut.Anomalies.Items).Gravite);
        Assert.Equal(0, parDefaut.Score);
        Assert.Equal("Bloquant", parDefaut.Statut);
        // Gravité ramenée à Mineure (1) : 100 − 100 × 1 / 5 = 80, aucune critique → Prêt.
        var anomalie = Assert.Single(modifie.Anomalies.Items);
        Assert.Equal("Mineure", anomalie.Gravite);
        Assert.Equal(1, anomalie.Poids);
        Assert.Equal(80, modifie.Score);
        Assert.Equal("Pret", modifie.Statut);
        Assert.Equal(1, modifie.ParGravite.Single(g => g.Cle == "Mineure").Nombre);
    }

    [Fact]
    public async Task ConfiguredMaxPeriod_IsEnforced()
    {
        var service = Service(Lus(("Closing:MaxPeriodDays", "7")));

        await Assert.ThrowsAsync<DomainException>(() =>
            service.AnalyserAsync(new ClosingAnalysisRequest(Lundi, Lundi.AddDays(7))));
        await service.AnalyserAsync(new ClosingAnalysisRequest(Lundi, Lundi.AddDays(6)));
    }

    [Fact]
    public async Task ExplicitRuleOptions_InRequest_TakePrecedenceOverSettings()
    {
        var service = Service(Lus(("Closing:Gravites:PointageManquant", "Mineure")));

        var resultat = await service.AnalyserAsync(new ClosingAnalysisRequest(Lundi, Lundi, Options: ClosingRulesOptions.Default));

        Assert.Equal("Critique", Assert.Single(resultat.Anomalies.Items).Gravite);
    }
}
