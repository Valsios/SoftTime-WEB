using Microsoft.Extensions.Configuration;

using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Lot E — signaux de vigilance (corrections manuelles massives, heures supplémentaires atypiques) : règles pures,
/// seuils réglables à défaut LARGE, niveau « Information » par défaut, donc score et statut mathématiquement
/// inchangés. Chaque règle a ses cas positif ET négatif.
/// </summary>
public class PayrollVigilanceRulesTests
{
    private static readonly DateTime Debut = new(2025, 11, 1);
    private static readonly DateTime Fin = new(2025, 11, 30);
    private static ClosingScope Scope(ClosingRulesOptions? o = null) => new(Debut, Fin, false, o ?? ClosingRulesOptions.Default);

    private static HeureCorrigee Correction(string matricule, DateTime jour, string? origine = "MANUEL") =>
        new() { MatriculeSage = matricule, DatePointageIn = jour, Origine = origine };

    private static HashSet<(string Cle, DateTime Jour)> Controles(string cle, int jours) =>
        Enumerable.Range(0, jours).Select(i => (cle, Debut.AddDays(i))).ToHashSet();

    private static HsValidee Semaine(string matricule, DateTime lundi, decimal hs, int id = 1, bool validee = true) =>
        new() { Id = id, Matricules = matricule, DateDeb = lundi, Hs = hs, ValidationHs = validee };

    // ---------- E1 : corrections manuelles massives ----------

    [Fact]
    public void CorrectionsManuelles_AboveMinimumAndRatio_IsOneInformationPerEmployee()
    {
        // Arrange : 6 jours corrigés à la main sur 10 contrôlés (≥ 5 et ≥ 50 %), une ligne en double ignorée.
        var corrections = Enumerable.Range(0, 6).Select(i => Correction("00777", Debut.AddDays(i))).ToList();
        corrections.Add(Correction("777", Debut.AddDays(5)));

        // Act
        var anomalies = PayrollVigilanceRules.CorrectionsManuelles(corrections, Controles("777", 10), Scope());

        // Assert
        var a = anomalies.Should().ContainSingle().Subject;
        a.Type.Should().Be(ClosingAnomalyType.CorrectionsManuelles);
        a.Severity.Should().Be(ClosingSeverity.Information);
        a.Matricule.Should().Be("777");
        a.Date.Should().Be(Debut.AddDays(5));                 // dernier jour corrigé à la main
        a.Detail.Should().Contain("6 jour(s) corrigé(s) à la main sur 10 jour(s) contrôlé(s)");
        a.Detail.Should().Contain("50 %").And.Contain("au moins 5 jours");
    }

    [Theory]
    [InlineData(4, 4)]     // sous le minimum, même à 100 %
    [InlineData(5, 20)]    // minimum atteint mais 25 % seulement
    public void CorrectionsManuelles_BelowEitherThreshold_IsNotReported(int manuels, int controles)
    {
        // Arrange
        var corrections = Enumerable.Range(0, manuels).Select(i => Correction("777", Debut.AddDays(i))).ToList();

        // Act / Assert
        PayrollVigilanceRules.CorrectionsManuelles(corrections, Controles("777", controles), Scope()).Should().BeEmpty();
    }

    [Fact]
    public void CorrectionsManuelles_AutomaticOrOutOfPeriod_AreIgnored()
    {
        // Arrange : corrections AUTO, sans origine, ou hors période — jamais comptées.
        var corrections = Enumerable.Range(0, 6).Select(i => Correction("777", Debut.AddDays(i), "AUTO"))
            .Concat(Enumerable.Range(0, 6).Select(i => Correction("777", Debut.AddDays(i), null)))
            .Concat(Enumerable.Range(1, 6).Select(i => Correction("777", Debut.AddDays(-i))))
            .ToList();

        // Act / Assert
        PayrollVigilanceRules.CorrectionsManuelles(corrections, Controles("777", 6), Scope()).Should().BeEmpty();
    }

    [Fact]
    public void CorrectionsManuelles_ConfiguredThresholds_AreApplied()
    {
        // Arrange : seuils resserrés (2 jours, 10 %).
        var o = ClosingRulesOptions.Default with { CorrectionsManuellesMinimum = 2, CorrectionsManuellesRatio = 0.1m };
        var corrections = new[] { Correction("777", Debut), Correction("777", Debut.AddDays(1)) };

        // Act / Assert
        PayrollVigilanceRules.CorrectionsManuelles(corrections, Controles("777", 20), Scope(o)).Should().ContainSingle();
    }

    // ---------- E2 : heures supplémentaires atypiques ----------

    [Fact]
    public void HeuresSup_ValidatedWeekAboveThreshold_IsInformationDatedOnSunday()
    {
        // Arrange : semaine du lundi 10 novembre, 41,5 h (seuil 40 h strictement dépassé).
        var lundi = new DateTime(2025, 11, 10);

        // Act
        var anomalies = PayrollVigilanceRules.HeuresSupAtypiques(new[] { Semaine("00777", lundi, 41.5m) }, Scope());

        // Assert
        var a = anomalies.Should().ContainSingle().Subject;
        a.Type.Should().Be(ClosingAnomalyType.HeuresSupAtypiques);
        a.Severity.Should().Be(ClosingSeverity.Information);
        a.Matricule.Should().Be("777");
        a.Date.Should().Be(lundi.AddDays(6));
        a.Heures.Should().Be(41.5m);
        a.Detail.Should().Be("semaine du 10/11 au 16/11 : 41,5 h supplémentaires validées (seuil : 40 h)");
    }

    [Theory]
    [InlineData(40, true)]      // égal au seuil : non signalé (dépassement strict)
    [InlineData(55, false)]     // non validée : non signalée
    public void HeuresSup_AtThresholdOrNotValidated_IsNotReported(double hs, bool validee)
    {
        // Arrange
        var semaine = Semaine("777", new DateTime(2025, 11, 10), (decimal)hs, validee: validee);

        // Act / Assert
        PayrollVigilanceRules.HeuresSupAtypiques(new[] { semaine }, Scope()).Should().BeEmpty();
    }

    [Fact]
    public void HeuresSup_WeekBelongsToThePeriodHoldingItsSunday()
    {
        // Arrange : lundi 27/10 → dimanche 02/11 (dans novembre) ; lundi 24/11 → dimanche 30/11 (dans) ;
        // lundi 01/12 → hors période ; lundi 20/10 → dimanche 26/10, hors période.
        var semaines = new[]
        {
            Semaine("1", new DateTime(2025, 10, 27), 50m, 1),
            Semaine("2", new DateTime(2025, 11, 24), 50m, 2),
            Semaine("3", new DateTime(2025, 12, 1), 50m, 3),
            Semaine("4", new DateTime(2025, 10, 20), 50m, 4),
        };

        // Act
        var anomalies = PayrollVigilanceRules.HeuresSupAtypiques(semaines, Scope());

        // Assert
        anomalies.Select(a => a.Matricule).Should().BeEquivalentTo("1", "2");
    }

    [Fact]
    public void HeuresSup_DuplicateValidationRows_GiveOneLineWithSmallestId()
    {
        // Arrange
        var lundi = new DateTime(2025, 11, 10);
        var semaines = new[] { Semaine("777", lundi, 48m, id: 9), Semaine("777", lundi, 45m, id: 3) };

        // Act
        var a = PayrollVigilanceRules.HeuresSupAtypiques(semaines, Scope()).Should().ContainSingle().Subject;

        // Assert
        a.Heures.Should().Be(45m);
    }

    // ---------- Neutralité par défaut : score et statut ----------

    [Fact]
    public void Defaults_NewTypes_AreInformationWithZeroWeight_SoScoreAndStatusAreUnchanged()
    {
        // Arrange : 1 critique sur 10 jours-employé, plus 3 signaux de vigilance.
        var critique = new ClosingAnomaly(ClosingAnomalyType.PointageManquant, ClosingSeverity.Critique, "1", Debut, "x");
        var vigilance = new[]
        {
            new ClosingAnomaly(ClosingAnomalyType.CorrectionsManuelles,
                PayrollClosingRules.GraviteParDefaut(ClosingAnomalyType.CorrectionsManuelles), "2", Debut, "x"),
            new ClosingAnomaly(ClosingAnomalyType.HeuresSupAtypiques,
                PayrollClosingRules.GraviteParDefaut(ClosingAnomalyType.HeuresSupAtypiques), "2", Debut, "x"),
            new ClosingAnomaly(ClosingAnomalyType.HeuresSupAtypiques,
                PayrollClosingRules.GraviteParDefaut(ClosingAnomalyType.HeuresSupAtypiques), "3", Debut, "x"),
        };
        var avec = vigilance.Prepend(critique).ToList();

        // Act / Assert
        PayrollClosingRules.Score(avec, 10).Should().Be(PayrollClosingRules.Score(new[] { critique }, 10));
        ClosingStatusRules.Statut(avec, 10, 0.05m).Should().Be(ClosingStatusRules.Statut(new[] { critique }, 10, 0.05m));
        vigilance.Should().OnlyContain(a => a.Severity == ClosingSeverity.Information);
    }

    // ---------- Réglages ----------

    private static ClosingOptions Lus(params (string Cle, string Valeur)[] valeurs)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(valeurs.Select(v => new KeyValuePair<string, string?>(v.Cle, v.Valeur)))
            .Build();
        var options = new ClosingOptions();
        new ClosingOptionsSetup(configuration).Configure(options);
        return options;
    }

    [Fact]
    public void Settings_Absent_UseLargeDefaults()
    {
        var o = Lus();
        o.CorrectionsManuellesRatio.Should().Be(0.5m);
        o.CorrectionsManuellesMinimum.Should().Be(5);
        o.HeuresSupHebdoSeuil.Should().Be(40m);
        o.Erreurs().Should().BeEmpty();
    }

    [Fact]
    public void Settings_Configured_ArePassedToRules()
    {
        // Act
        var regles = Lus(("Closing:CorrectionsManuellesRatio", "0.25"), ("Closing:CorrectionsManuellesMinimum", "3"),
            ("Closing:HeuresSupHebdoSeuil", "12.5"), ("Closing:Gravites:HeuresSupAtypiques", "Importante")).ReglesParDefaut();

        // Assert
        regles.CorrectionsManuellesRatio.Should().Be(0.25m);
        regles.CorrectionsManuellesMinimum.Should().Be(3);
        regles.HeuresSupHebdoSeuil.Should().Be(12.5m);
        regles.Gravite(ClosingAnomalyType.HeuresSupAtypiques).Should().Be(ClosingSeverity.Importante);
    }

    [Theory]
    [InlineData("Closing:CorrectionsManuellesRatio", "1.5", "CorrectionsManuellesRatio doit être compris entre 0 et 1")]
    [InlineData("Closing:CorrectionsManuellesRatio", "moitié", "CorrectionsManuellesRatio doit être un nombre décimal")]
    [InlineData("Closing:CorrectionsManuellesMinimum", "0", "CorrectionsManuellesMinimum doit être au moins 1")]
    [InlineData("Closing:HeuresSupHebdoSeuil", "0", "HeuresSupHebdoSeuil doit être strictement positif")]
    [InlineData("Closing:HeuresSupHebdoSeuil", "quarante", "HeuresSupHebdoSeuil doit être un nombre décimal")]
    public void Settings_Invalid_RefuseStartupWithClearMessage(string cle, string valeur, string attendu)
    {
        Lus((cle, valeur)).Erreurs().Should().ContainSingle().Which.Should().Contain(attendu);
    }
}
