using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.Sage;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Integrations;
using SoftTime.Infrastructure.Persistence;
using SoftTime.Infrastructure.Services.Closing;
using SoftTime.Infrastructure.Services.Payroll;

namespace SoftTime.Tests;

/// <summary>
/// Lot C3 : envoi de l'Absence (non justifiée) à la paie, configurable par le RH.
/// <list type="bullet">
///   <item>calcul : un jour planifié sans aucune ligne est toujours compté ; une journée couverte seulement par des
///     absences arbitrées n'est comptée que si l'une porte un code compté (défaut : ABSENCE NON JUSTIFIEE et l'arbitrage
///     sans code) ; demi-journées inchangées ;</item>
///   <item>réglage : unité heures ou jours, heures par jour (défaut 8, entre 1 et 24, une décimale), codes comptés ; mêmes
///     droit, verrou de mois, journal, caches ; enregistrement atomique dans le même appel ;</item>
///   <item>envoi : valeur affichée (heures), convertie en jours au centième (demi vers le haut) ; STANDARD et AUTRE ;
///     remise à 0 ; variable décochée jamais écrite ; Retard et les sept variables d'heures supplémentaires inchangés.</item>
/// </list>
/// </summary>
public class PayrollAbsenceC3Tests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Lundi = new(2026, 9, 14);
    private static readonly OvertimeWeekConfig Config = new(MultiPointage: true, PauseCategorie: TimeSpan.FromMinutes(30));

    // =====================================================================================================
    // Calcul de l'Absence selon les codes comptés
    // =====================================================================================================

    private static PlanningSalarie Plan(DateTime jour, bool off = false) => new()
    {
        Id = 1, IdSal = 1, DateP = jour, Ha = jour.Date.AddHours(8), Hd = jour.Date.AddHours(17), Pause = TimeSpan.FromMinutes(30), OffP = off
    };

    private static HeureCorrigee Arbitrage(string? code, bool am = true, bool pm = true) => new()
    {
        MatriculeSage = "001", DatePointageIn = Lundi, DatePointageOut = Lundi, AbsenceAm = am, AbsencePm = pm, IntituleAbsence = code
    };

    private static decimal Absence(IEnumerable<HeureCorrigee> lignes, AbsenceCodesComptes? codes = null,
        bool ferie = false, decimal conge = 0m, bool off = false, bool multi = true)
    {
        var jour = OvertimeDayRules.Construire(new CalendarDay(Lundi, ferie, conge), lignes, Plan(Lundi, off), codes);
        return OvertimeCalculator.CalculerJour(Config with { MultiPointage = multi }, jour).Absence;
    }

    [Fact]
    public void Calcul_DayWithoutAnyLine_IsAlwaysCounted_WhateverTheSetting()
    {
        Absence([]).Should().Be(8.5m);
        Absence([], new AbsenceCodesComptes([], SansCode: false)).Should().Be(8.5m, "un jour sans aucune ligne est toujours compté");
        Absence([], multi: false).Should().Be(8.5m);
    }

    [Fact]
    public void Calcul_ArbitrationWithACountedCode_IsCounted()
        => Absence([Arbitrage("ABSENCE NON JUSTIFIEE")]).Should().Be(8.5m);

    [Fact]
    public void Calcul_ArbitrationWithANotCountedCode_IsJustified_NotCounted()
    {
        Absence([Arbitrage("REPOS MALADIE")]).Should().Be(0m);
        Absence([Arbitrage("PERMISSION")], multi: false).Should().Be(0m);
    }

    [Fact]
    public void Calcul_ArbitrationWithoutCode_FollowsTheSansCodeSetting()
    {
        Absence([Arbitrage(null)]).Should().Be(8.5m, "par défaut, l'arbitrage sans code est compté");
        Absence([Arbitrage("   ")]).Should().Be(8.5m);
        Absence([Arbitrage(null)], new AbsenceCodesComptes([AbsenceCodesComptes.AbsenceNonJustifiee], SansCode: false)).Should().Be(0m);
    }

    [Fact]
    public void Calcul_HalfDayArbitrationWithoutHours_StillCountsTheWholeDay_AndPresenceOnTheOtherHalfCountsZero()
    {
        // Demi-journées inchangées (alignement sur le rapport des absences reporté).
        Absence([Arbitrage("ABSENCE NON JUSTIFIEE", am: true, pm: false)]).Should().Be(8.5m);
        var presenceApresMidi = new HeureCorrigee
        {
            MatriculeSage = "001", DatePointageIn = Lundi, DatePointageOut = Lundi, HEntree = TimeSpan.FromHours(13), HSortie = TimeSpan.FromHours(17)
        };
        Absence([Arbitrage("ABSENCE NON JUSTIFIEE", am: true, pm: false), presenceApresMidi]).Should().Be(0m);
    }

    [Fact]
    public void Calcul_SeveralArbitrations_OneCountedCodeIsEnough()
    {
        Absence([Arbitrage("PERMISSION", am: true, pm: false), Arbitrage("ABSENCE NON JUSTIFIEE", am: false, pm: true)]).Should().Be(8.5m);
        Absence([Arbitrage("PERMISSION", am: true, pm: false), Arbitrage("MISSION", am: false, pm: true)]).Should().Be(0m);
    }

    [Fact]
    public void Calcul_SageLeave_Holiday_AndOffDay_AreNeverCounted()
    {
        Absence([], conge: 1m).Should().Be(0m);
        Absence([], ferie: true).Should().Be(0m);
        Absence([], off: true).Should().Be(0m);
        Absence([Arbitrage("ABSENCE NON JUSTIFIEE")], ferie: true).Should().Be(0m);
    }

    [Fact]
    public void Calcul_NoSetting_UsesTheDefault()
    {
        AbsenceCodesComptes.Default.Codes.Should().Equal("ABSENCE NON JUSTIFIEE");
        AbsenceCodesComptes.Default.SansCode.Should().BeTrue();
        PayrollOptions.AbsenceCodes([]).Should().BeSameAs(AbsenceCodesComptes.Default);
        Absence([Arbitrage("ABSENCE NON JUSTIFIEE")], codes: null).Should().Be(8.5m);
        Absence([Arbitrage("MISE A PIED")], codes: null).Should().Be(0m);
    }

    [Theory]
    [InlineData("absence non justifiée")]
    [InlineData("  ABSENCE   NON  JUSTIFIEE ")]
    [InlineData("Absence Non Justifiee")]
    [InlineData("ABSENCE NON JUSTIFIÉE")]
    public void Calcul_LabelMatching_IgnoresCaseAccentsAndExtraSpaces(string libelle)
    {
        AbsenceCodesComptes.Default.Compte(libelle).Should().BeTrue();
        Absence([Arbitrage(libelle)]).Should().Be(8.5m);
    }

    [Fact]
    public void Calcul_RenamedOrMissingCode_MatchesOnlyTheStoredLabel()
    {
        // Le réglage garde « Abs non payés » ; une ligne ancienne porte ce libellé, une nouvelle le libellé renommé.
        var codes = new AbsenceCodesComptes(["Abs non payés"], SansCode: false);
        Absence([Arbitrage("ABS NON PAYES")], codes).Should().Be(8.5m);
        Absence([Arbitrage("Absence non payée")], codes).Should().Be(0m, "un code renommé n'est plus reconnu tant qu'il n'est pas coché");
    }

    [Fact]
    public void Options_StoredCodes_AreReadInRankOrder_WithTheSansCodeMarker()
    {
        var options = new[]
        {
            new PaieOption { BddSage = "B", Cle = "AbsenceCodeCompte.2", Valeur = "MISE A PIED" },
            new PaieOption { BddSage = "B", Cle = "AbsenceSansCodeCompte", Valeur = "0" },
            new PaieOption { BddSage = "B", Cle = "AbsenceCodeCompte.1", Valeur = "ABSENCE NON JUSTIFIEE" },
        };

        var codes = PayrollOptions.AbsenceCodes(options);

        codes.Codes.Should().Equal("ABSENCE NON JUSTIFIEE", "MISE A PIED");
        codes.SansCode.Should().BeFalse();
        // Marqueur seul : réglage enregistré sans aucun code (seuls les jours sans ligne et l'arbitrage sans code comptent).
        var marqueurSeul = PayrollOptions.AbsenceCodes([new PaieOption { BddSage = "B", Cle = "AbsenceSansCodeCompte", Valeur = "1" }]);
        marqueurSeul.Codes.Should().BeEmpty();
        marqueurSeul.SansCode.Should().BeTrue();
        marqueurSeul.Compte("ABSENCE NON JUSTIFIEE").Should().BeFalse();
    }

    // =====================================================================================================
    // Unité, heures par jour, arrondi
    // =====================================================================================================

    [Theory]
    [InlineData("24", "8", "3")]
    [InlineData("25.5", "8", "3.19")]
    [InlineData("24", "6", "4")]
    [InlineData("1", "8", "0.13")]      // 0,125 : la moitié est arrondie vers le haut
    [InlineData("3", "8", "0.38")]      // 0,375
    [InlineData("0", "8", "0")]
    [InlineData("10", "7.5", "1.33")]
    public void EnJours_RoundsToTheHundredth_HalfUp(string heures, string parJour, string attendu)
        => PayrollUnits.EnJours(decimal.Parse(heures, System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(parJour, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(decimal.Parse(attendu, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Selection_ValueSent_IsInHoursByDefault_InDaysWhenChosen()
    {
        var ligne = new PayrollRowValues(1, 2, 3, 4, 5, 6, 7, 2.75m, 25.5m);
        var heures = PayrollVariableSelection.Avec([PayrollVariableSelection.Absence]);
        var jours = heures with { Unites = new Dictionary<string, string> { ["Absence"] = "J" } };

        heures.ValeurEnvoyee("Absence", ligne).Should().Be(25.5m);
        jours.ValeurEnvoyee("Absence", ligne).Should().Be(3.19m);
        (jours with { HeuresParJour = 6m }).ValeurEnvoyee("Absence", ligne).Should().Be(4.25m);
        jours.ValeurEnvoyee("Retard", ligne).Should().Be(2.75m, "Retard reste en heures");
        jours.ValeurEnvoyee("Exo130", ligne).Should().Be(1m);
        (heures with { Unites = new Dictionary<string, string> { ["Absence"] = "X" } }).UniteDe("Absence").Should().Be("H", "unité inconnue : heures");
    }

    [Theory]
    [InlineData("8", true)]
    [InlineData("1", true)]
    [InlineData("24", true)]
    [InlineData("7.5", true)]
    [InlineData("0.5", false)]
    [InlineData("0", false)]
    [InlineData("24.5", false)]
    [InlineData("7.25", false)]
    [InlineData("-8", false)]
    public void HeuresParJour_Between1And24_OneDecimalAtMost(string valeur, bool valide)
    {
        var v = decimal.Parse(valeur, System.Globalization.CultureInfo.InvariantCulture);
        PayrollOptions.HeuresParJourValide(v).Should().Be(valide);
        (PayrollVariableRules.ErreurHeuresParJour(v) is null).Should().Be(valide);
    }

    [Fact]
    public void HeuresParJour_DefaultsTo8_WhenAbsentOrUnreadable()
    {
        PayrollOptions.HeuresParJour([]).Should().Be(8m);
        PayrollOptions.HeuresParJour([new PaieOption { Cle = "AbsenceHeuresParJour", Valeur = "abc" }]).Should().Be(8m);
        PayrollOptions.HeuresParJour([new PaieOption { Cle = "AbsenceHeuresParJour", Valeur = "30" }]).Should().Be(8m);
        PayrollOptions.HeuresParJour([new PaieOption { Cle = "AbsenceHeuresParJour", Valeur = "7.5" }]).Should().Be(7.5m);
        PayrollVariableSelection.Default.HeuresParJour.Should().Be(8m);
    }

    // =====================================================================================================
    // Réglage (service) : sélection, refus, droit, 409, atomicité, journal, caches
    // =====================================================================================================

    [Fact]
    public async Task Save_AbsenceWithASageCode_InDays_IsStored_AndSent()
    {
        var c = Creer(codes: [new("ABS01", "Absences")]);

        var resultat = await c.Sut.SaveAsync(Demande(("Absence", true, "abs01", "j")) with { HeuresParJour = 7.5m });

        var ligne = c.Uow.Get<PaieVariable>().Items.Single(v => v.Variable == "Absence");
        ligne.Should().Match<PaieVariable>(v => v.Actif && v.CodePaie == "ABS01" && v.Unite == "J" && v.ModifiePar == "u1");
        c.Uow.Get<PaieOption>().Items.Single(o => o.Cle == "AbsenceHeuresParJour").Valeur.Should().Be("7.5");
        var selection = await c.Sut.GetSelectionAsync("SAGE_TEST");
        selection.Envoyees.Should().Equal([.. PayrollVariableSelection.HeuresSup, PayrollVariableSelection.Absence]);
        selection.CodeDe("Absence").Should().Be("ABS01");
        selection.UniteDe("Absence").Should().Be("J");
        selection.HeuresParJour.Should().Be(7.5m);
        resultat.HeuresParJour.Should().Be(7.5m);
        resultat.Variables.Single(v => v.Variable == "Absence").Should().Match<PayrollVariableDto>(v => v.Actif && v.CodePaie == "ABS01" && v.Unite == "J" && !v.SansEnvoi);
    }

    [Fact]
    public async Task Save_AbsenceChecked_WithoutCode_IsRefused()
    {
        var c = Creer(codes: [new("ABS01", "Absences")]);

        var act = () => c.Sut.SaveAsync(Demande(("Absence", true, null, "H")));

        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message
            .Should().Be("La variable Absence ne peut pas être cochée tant que son code de paie n'est pas renseigné.");
        c.Uow.Get<PaieVariable>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_AbsenceWithAnUnknownSageCode_IsRefused_LikeRetard()
    {
        var c = Creer(codes: [new("ABS01", "Absences")]);

        var act = () => c.Sut.SaveAsync(Demande(("Absence", true, "XX99", "H")));

        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message
            .Should().Be("Le code de paie XX99 de la variable Absence n'existe pas dans la base SAGE SAGE_TEST.");
        c.Uow.Get<PaieVariable>().Items.Should().BeEmpty();
        c.Uow.Get<PaieOption>().Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Absence", "X", "L'unité de la variable Absence doit être « heures » ou « jours ».")]
    [InlineData("Retard", "J", "L'unité de la variable Retard doit être « heures ».")]
    public async Task Save_UnknownUnit_IsRefused(string variable, string unite, string message)
    {
        var c = Creer(codes: [new("ABS01", "Absences"), new("RET01", "Retard")]);

        var act = () => c.Sut.SaveAsync(Demande((variable, false, variable == "Absence" ? "ABS01" : "RET01", unite)));

        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Contain(message);
    }

    [Fact]
    public async Task Save_AbsenceChecked_WithoutUnit_IsRefused()
    {
        var c = Creer(codes: [new("ABS01", "Absences")]);

        var act = () => c.Sut.SaveAsync(Demande(("Absence", true, "ABS01", null)));

        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Contain("Absence ne peut pas être cochée sans unité (heures ou jours)");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("25")]
    [InlineData("7.25")]
    public async Task Save_InvalidHoursPerDay_IsRefused_AndNothingIsWritten(string valeur)
    {
        var c = Creer(codes: [new("ABS01", "Absences")]);
        var heures = decimal.Parse(valeur, System.Globalization.CultureInfo.InvariantCulture);

        var act = () => c.Sut.SaveAsync(Demande(("Absence", true, "ABS01", "J")) with { HeuresParJour = heures });

        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message
            .Should().Be($"Le nombre d'heures par jour de l'absence doit être compris entre 1 et 24, avec une décimale au plus (valeur reçue : {valeur}).");
        c.Uow.Get<PaieVariable>().Items.Should().BeEmpty();
        c.Uow.Get<PaieOption>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_HoursPerDay_Default8_CreatesNoRow_AbsentField_LeavesItUnchanged()
    {
        var c = Creer();
        await c.Sut.SaveAsync(Demande() with { HeuresParJour = 8m });
        c.Uow.Get<PaieOption>().Items.Should().BeEmpty("8 est la valeur par défaut");
        (await c.Sut.GetConfigurationAsync()).HeuresParJour.Should().Be(8m);

        await c.Sut.SaveAsync(Demande() with { HeuresParJour = 6m });
        await c.Sut.SaveAsync(Demande());
        c.Uow.Get<PaieOption>().Items.Single(o => o.Cle == "AbsenceHeuresParJour").Valeur.Should().Be("6");
    }

    [Fact]
    public async Task Save_AbsenceCodes_StoredOneRowPerCode_WithTheLabelOfTCodeAbsence()
    {
        var c = Creer();
        CodesAbsence(c.Uow);

        var resultat = await c.Sut.SaveAsync(Demande() with
        {
            AbsenceCodes = new PayrollAbsenceCodesSaveDto(false, ["absence non justifiée", "  mise a pied ", "MISE A PIED"])
        });

        var options = c.Uow.Get<PaieOption>().Items.Where(o => o.BddSage == "SAGE_TEST").OrderBy(o => o.Cle).ToList();
        options.Select(o => (o.Cle, o.Valeur)).Should().Equal(
            ("AbsenceCodeCompte.1", "ABSENCE NON JUSTIFIEE"), ("AbsenceCodeCompte.2", "MISE A PIED"), ("AbsenceSansCodeCompte", "0"));
        options.Should().OnlyContain(o => o.ModifiePar == "u1" && o.ModifieLe == Maintenant.UtcDateTime);
        resultat.AbsenceCodes!.ParDefaut.Should().BeFalse();
        resultat.AbsenceCodes.SansCode.Should().BeFalse();
        resultat.AbsenceCodes.Codes.Where(x => x.Compte).Select(x => x.Libelle).Should().Equal("MISE A PIED", "ABSENCE NON JUSTIFIEE");
    }

    [Fact]
    public async Task Save_AbsenceCodes_DefaultWithoutAnyRow_CreatesNothing_RemovedCodes_AreDeleted()
    {
        var c = Creer();
        CodesAbsence(c.Uow);
        await c.Sut.SaveAsync(Demande() with { AbsenceCodes = new PayrollAbsenceCodesSaveDto(true, ["Absence non justifiee"]) });
        c.Uow.Get<PaieOption>().Items.Should().BeEmpty("réglage égal à la valeur par défaut");

        await c.Sut.SaveAsync(Demande() with { AbsenceCodes = new PayrollAbsenceCodesSaveDto(true, ["ABSENCE NON JUSTIFIEE", "MISE A PIED", "PERMISSION"]) });
        await c.Sut.SaveAsync(Demande() with { AbsenceCodes = new PayrollAbsenceCodesSaveDto(true, ["MISE A PIED"]) });

        c.Uow.Get<PaieOption>().Items.Select(o => (o.Cle, o.Valeur)).OrderBy(x => x.Cle).Should().Equal(
            ("AbsenceCodeCompte.1", "MISE A PIED"), ("AbsenceSansCodeCompte", "1"));
    }

    [Fact]
    public async Task Save_UnknownAbsenceCode_IsRefused_ButAStoredCodeMissingFromTCodeAbsence_IsKept()
    {
        var c = Creer();
        CodesAbsence(c.Uow);
        c.Uow.Get<PaieOption>().Items.AddRange([
            new PaieOption { BddSage = "SAGE_TEST", Cle = "AbsenceSansCodeCompte", Valeur = "1" },
            new PaieOption { BddSage = "SAGE_TEST", Cle = "AbsenceCodeCompte.1", Valeur = "ANCIEN CODE" },
        ]);

        var inconnu = () => c.Sut.SaveAsync(Demande() with { AbsenceCodes = new PayrollAbsenceCodesSaveDto(true, ["INVENTE"]) });
        (await inconnu.Should().ThrowExactlyAsync<DomainException>()).Which.Message
            .Should().Be("Le code d'absence « INVENTE » n'existe pas dans les codes d'absence.");

        var config = await c.Sut.GetConfigurationAsync();
        config.AbsenceCodes!.Codes.Single(x => x.Libelle == "ANCIEN CODE").Should().Match<PayrollAbsenceCodeDto>(x => x.Compte && x.Introuvable);

        await c.Sut.SaveAsync(Demande() with { AbsenceCodes = new PayrollAbsenceCodesSaveDto(true, ["ancien code", "MISE A PIED"]) });
        PayrollOptions.AbsenceCodes(c.Uow.Get<PaieOption>().Items).Codes.Should().Equal("ANCIEN CODE", "MISE A PIED");
    }

    [Fact]
    public async Task GetConfiguration_ListsTheCodesOfTCodeAbsence_WithDefault_NotPay_AndSuggestions()
    {
        var c = Creer();
        CodesAbsence(c.Uow);

        var codes = (await c.Sut.GetConfigurationAsync()).AbsenceCodes!;

        codes.ParDefaut.Should().BeTrue();
        codes.SansCode.Should().BeTrue();
        codes.Codes.Select(x => x.Libelle).Should().Equal("Abs non payés", "PERMISSION", "MISE A PIED", "ABSENCE NON JUSTIFIEE"); // trié par IdAbsence (7, 1027, 1029, 1031), l'ordre de T_CODEABSENCE
        codes.Codes.Where(x => x.Compte).Select(x => x.Libelle).Should().Equal("ABSENCE NON JUSTIFIEE");
        codes.Codes.Single(x => x.Libelle == "Abs non payés").Should().Match<PayrollAbsenceCodeDto>(x => x.NonPaye && x.Suggere && !x.Compte,
            "un code NOT_PAY = 1 n'est jamais coché par défaut ; il est seulement suggéré");
        codes.Codes.Single(x => x.Libelle == "PERMISSION").Suggere.Should().BeFalse();
    }

    [Fact]
    public async Task Save_Absence_WhileCurrentMonthSent_IsRefusedWith409_AndWritesNothing()
    {
        var c = Creer(codes: [new("ABS01", "Absences")]);
        CodesAbsence(c.Uow);
        c.Envois.Periodes.Add((new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)));

        var act = () => c.Sut.SaveAsync(Demande(("Absence", true, "ABS01", "J")) with
        {
            HeuresParJour = 7m, AbsenceCodes = new PayrollAbsenceCodesSaveDto(false, ["MISE A PIED"])
        });

        (await act.Should().ThrowExactlyAsync<ConflictException>()).Which.Message.Should().StartWith("Mois déjà envoyé en paie");
        c.Uow.Get<PaieVariable>().Items.Should().BeEmpty();
        c.Uow.Get<PaieOption>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_Absence_WithoutTheRightToModifyTheBase_IsForbidden()
    {
        var c = Creer(codes: [new("ABS01", "Absences")]);
        c.Autorisation.Setup(a => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, "SAGE_TEST", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("refus"));

        var act = () => c.Sut.SaveAsync(Demande(("Absence", true, "ABS01", "H")) with { HeuresParJour = 7m });

        await act.Should().ThrowExactlyAsync<ForbiddenException>();
        c.Uow.Get<PaieOption>().Items.Should().BeEmpty();
        c.Uow.Get<PaieVariable>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_IsAtomic_AnInvalidCode_WritesNeitherTheVariablesNorTheOptions()
    {
        var c = Creer(codes: [new("ABS01", "Absences")]);
        CodesAbsence(c.Uow);

        var act = () => c.Sut.SaveAsync(Demande(("Absence", true, "ABS01", "J")) with
        {
            HeuresParJour = 7m, RemiseAZeroSansValeur = false, AbsenceCodes = new PayrollAbsenceCodesSaveDto(false, ["INVENTE"])
        });

        await act.Should().ThrowExactlyAsync<DomainException>();
        c.Uow.Get<PaieVariable>().Items.Should().BeEmpty();
        c.Uow.Get<PaieOption>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_InvalidatesTheSelectionCache_AndTheClosingCaches()
    {
        var c = Creer(codes: [new("ABS01", "Absences")]);
        (await c.Sut.GetSelectionAsync("SAGE_TEST")).EstActive("Absence").Should().BeFalse(); // mis en cache
        var cle = new ClosingChecklistRequest(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), null);
        c.Checklist.Set("cle", new ClosingChecklistDto(cle.From, cle.To, null, []), Maintenant, TimeSpan.FromMinutes(5));

        await c.Sut.SaveAsync(Demande(("Absence", true, "ABS01", "J")) with { HeuresParJour = 6m });

        var selection = await c.Sut.GetSelectionAsync("SAGE_TEST");
        selection.EstActive("Absence").Should().BeTrue();
        selection.HeuresParJour.Should().Be(6m);
        c.Checklist.TryGet("cle", Maintenant, out _).Should().BeFalse();
        var actives = await c.Sut.GetActivesAsync();
        actives.UniteAbsence.Should().Be("J");
        actives.HeuresParJour.Should().Be(6m);
    }

    [Fact]
    public async Task Save_OtherBase_AbsenceCheckedWithItsDestination_IsAccepted()
    {
        var c = Creer(typeBase: "AUTRE");
        c.Uow.Get<PaieEcriture>().Items.Add(new PaieEcriture
        {
            BddSage = "SAGE_AUTRE", TableCible = "PAIE_CUMULS", ColMatricule = "Matricule", Categorie = "Absence", ColValeur = "JoursAbsence"
        });

        var resultat = await c.Sut.SaveAsync(Demande(("Absence", true, null, "J")));

        resultat.Variables.Single(v => v.Variable == "Absence").Should().Match<PayrollVariableDto>(v => v.Actif && v.CibleExistante == "JoursAbsence" && v.Unite == "J");
        var sansDestination = Creer(typeBase: "AUTRE");
        var act = () => sansDestination.Sut.SaveAsync(Demande(("Absence", true, null, "J")));
        (await act.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Contain("Absence ne peut pas être cochée tant que sa colonne cible n'est pas renseignée");
    }

    [Fact]
    public async Task SaveDestinations_OtherBase_AbsenceColumn_MustBeNumericWithDecimals()
    {
        var c = Creer(typeBase: "AUTRE");
        c.Uow.Get<PaieEcriture>().Items.AddRange(PayrollVariableSelection.HeuresSup.Select(v => new PaieEcriture
        {
            BddSage = "SAGE_AUTRE", TableCible = "PAIE_CUMULS", ColMatricule = "Matricule", Categorie = v, ColValeur = $"Heures{v}"
        }));

        var entier = () => c.Sut.SaveDestinationsAsync(new PayrollWriteTargetsSaveDto("PAIE_CUMULS", "Matricule", [new("Absence", "Entier")]));
        var texte = () => c.Sut.SaveDestinationsAsync(new PayrollWriteTargetsSaveDto("PAIE_CUMULS", "Matricule", [new("Absence", "Libelle")]));
        var inconnue = () => c.Sut.SaveDestinationsAsync(new PayrollWriteTargetsSaveDto("PAIE_CUMULS", "Matricule", [new("Absence", "Inexistante")]));

        (await entier.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().StartWith("La colonne Entier de la variable Absence n'accepte pas 2 décimales");
        (await texte.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Be("La colonne Libelle de la variable Absence n'est pas numérique (nvarchar).");
        (await inconnue.Should().ThrowExactlyAsync<DomainException>()).Which.Message.Should().Be("La colonne Inexistante de la variable Absence n'existe pas dans la table PAIE_CUMULS.");
        await c.Sut.SaveDestinationsAsync(new PayrollWriteTargetsSaveDto("PAIE_CUMULS", "Matricule", [new("Absence", "JoursAbsence")]));
        c.Uow.Get<PaieEcriture>().Items.Single(e => e.Categorie == "Absence").ColValeur.Should().Be("JoursAbsence");
    }

    [Fact]
    public void Journal_ModifiedHoursPerDayAndCodes_RecordWhoBeforeAndAfter()
    {
        // Arrange
        using var db = new SoftTimeDbContext(new DbContextOptionsBuilder<SoftTimeDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SoftTimePayrollVariablesJournal;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
        var heures = new PaieOption { Id = 3, BddSage = "SAGE_TEST", Cle = PayrollOptions.AbsenceHeuresParJour, Valeur = "8" };
        db.Attach(heures);
        heures.Valeur = "7.5";
        db.Entry(heures).Property(o => o.Valeur).IsModified = true;
        db.Add(new PaieOption { BddSage = "SAGE_TEST", Cle = "AbsenceCodeCompte.1", Valeur = "MISE A PIED" });

        // Act
        new AuditJournalSaveChangesInterceptor(new TestCurrentUser(1, 1), Mock.Of<ICompanyContext>(), Options.Create(new AuditJournalOptions()))
            .CaptureChanges(db);

        // Assert
        var entrees = db.ChangeTracker.Entries<JournalAudit>().Select(e => e.Entity).ToList();
        // Une modification = une entrée (colonne VALEUR) ; une insertion = une entrée par colonne de la ligne (5 colonnes de T_PAIE_OPTION).
        entrees.Should().HaveCount(6).And.OnlyContain(e => e.Tables == "T_PAIE_OPTION" && e.Users == "u1");
        entrees.Where(e => e.Actions == "Insertion").Should().HaveCount(5);
        entrees.Should().ContainSingle(e => e.Actions != "Insertion" && e.Colonnes == "VALEUR" && e.Anciens == "8" && e.Nouveaux == "7.5");
        entrees.Should().Contain(e => e.Actions == "Insertion" && e.Colonnes == "CLE" && e.Nouveaux == "AbsenceCodeCompte.1");
        entrees.Should().Contain(e => e.Actions == "Insertion" && e.Colonnes == "VALEUR" && e.Nouveaux == "MISE A PIED");
    }

    // =====================================================================================================
    // Envoi
    // =====================================================================================================

    private static readonly HsExoDto Ligne = new(1, "MAT01", 101, 1.5m, 2m, 0.5m, 1.25m, 3m, 4m, 2.5m, 7m, 25.5m);

    private static PayrollVariableSelection AvecAbsence(string unite, decimal heuresParJour = 8m, bool retard = false) =>
        PayrollVariableSelection.Avec([.. PayrollVariableSelection.HeuresSup, .. (retard ? new[] { "Retard" } : []), "Absence"]) with
        {
            CodesPaie = new Dictionary<string, string> { ["Absence"] = "ABS01", ["Retard"] = "RET01" },
            Unites = new Dictionary<string, string> { ["Absence"] = unite },
            HeuresParJour = heuresParJour
        };

    private static SagePayrollTarget Cible(PayrollVariableSelection selection) => new("test",
        Enumerable.Range(1, 7)
            .Select(i => new ConstanteSage { CodeConstante = $"HS0{i}", CodeOperande1 = (short)i })
            .Append(new ConstanteSage { CodeConstante = "RET01", CodeOperande1 = 8 })
            .Append(new ConstanteSage { CodeConstante = "ABS01", CodeOperande1 = 9 })
            .ToArray(),
        new OvertimeSageCodes()) { Variables = selection };

    [Theory]
    [InlineData("H", "8", "25.5")]
    [InlineData("J", "8", "3.19")]
    [InlineData("J", "6", "4.25")]
    public async Task Standard_AbsenceIsWrittenInItsSageCode_InTheChosenUnit_AfterTheSevenAndRetard(string unite, string parJour, string attendu)
    {
        var sage = new CapturingSagePayrollWriter();
        var selection = AvecAbsence(unite, decimal.Parse(parJour, System.Globalization.CultureInfo.InvariantCulture), retard: true);

        var updates = await sage.SyncOvertimeAsync(Cible(selection), 101, OvertimeAmounts.From(Ligne));

        sage.Calls.Should().Equal(
            (101, (short)1, 1.5m), (101, (short)2, 2m), (101, (short)3, 0.5m), (101, (short)4, 1.25m),
            (101, (short)5, 3m), (101, (short)6, 2.5m), (101, (short)7, 4m), (101, (short)8, 7m),
            (101, (short)9, decimal.Parse(attendu, System.Globalization.CultureInfo.InvariantCulture)));
        updates.Keys.Should().Equal("Exo130", "Exo150", "I130", "I150", "Ferie", "Dimanche", "Nuit", "Retard", "Absence");
    }

    [Fact]
    public async Task Standard_24HoursAt8_Writes3Days_At6_Writes4Days()
    {
        var ligne = Ligne with { Absence = 24m };
        var huit = new CapturingSagePayrollWriter();
        var six = new CapturingSagePayrollWriter();

        await huit.SyncOvertimeAsync(Cible(AvecAbsence("J")), 103, OvertimeAmounts.From(ligne));
        await six.SyncOvertimeAsync(Cible(AvecAbsence("J", 6m)), 103, OvertimeAmounts.From(ligne));

        huit.Calls.Last().Should().Be((103, (short)9, 3m));
        six.Calls.Last().Should().Be((103, (short)9, 4m));
    }

    [Fact]
    public async Task Standard_UncheckedAbsence_IsNeverWritten_NorResolved_EvenWithACode()
    {
        var sage = new CapturingSagePayrollWriter();
        var selection = PayrollVariableSelection.Default with
        {
            CodesPaie = new Dictionary<string, string> { ["Absence"] = "INCONNU" }, Unites = new Dictionary<string, string> { ["Absence"] = "J" }
        };

        await sage.SyncOvertimeAsync(Cible(selection), 101, OvertimeAmounts.From(Ligne));

        sage.Calls.Select(c => c.Operand).Should().Equal((short)1, 2, 3, 4, 5, 6, 7);
    }

    [Fact]
    public async Task Standard_CheckedAbsence_IsResetToZero_ForAnEmployeeWithoutValue_AndCountedInTheJournal()
    {
        var sage = new CapturingSagePayrollWriter();
        var audit = new CapturingAudit();
        var envois = new RecordingSendLog();
        var sansValeur = new HsExoDto(0, "MAT02", 102, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m) { SansValeur = true };

        var resultat = await new TestStandardWriter(sage, audit, new FixedVariableService(AvecAbsence("J")), envois)
            .WriteAsync(new BddSage { NomBd = "SAGE_TEST", TypeBase = "STANDARD" }, [Ligne, sansValeur], Contexte());

        sage.Calls.Where(c => c.NumSal == 102).Should().Equal(Enumerable.Range(1, 7).Select(op => (102, (short)op, 0m)).Append((102, (short)9, 0m)));
        resultat.CategoriesUpdated.Should().Be(16);
        resultat.EmployeesReset.Should().Be(1);
        audit.Audits.Should().ContainSingle().Which.UpdatesByCategory.Should().Contain(new KeyValuePair<string, int>("Absence", 2));
        envois.Envois.Should().ContainSingle().Which.Lines.Should().Be(16);
    }

    [Fact]
    public async Task Standard_CheckedAbsenceWithAMissingSageConstant_FailsBeforeAnyWrite()
    {
        var sage = new CapturingSagePayrollWriter();
        var selection = AvecAbsence("H") with { CodesPaie = new Dictionary<string, string> { ["Absence"] = "XX99" } };

        var act = () => sage.SyncOvertimeAsync(Cible(selection), 101, OvertimeAmounts.From(Ligne));

        (await act.Should().ThrowAsync<PayrollSyncWriteException>()).Which.InnerException!.Message.Should().Contain("XX99");
        sage.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Standard_DefaultSelection_IsUnchanged_SevenVariablesInHours()
    {
        PayrollVariableSelection.Default.Envoyees.Should().Equal(PayrollVariableSelection.HeuresSup);
        PayrollVariableSelection.Default.EstActive("Absence").Should().BeFalse("l'Absence reste décochée par défaut");
        foreach (var v in PayrollVariableSelection.HeuresSup)
            PayrollVariableSelection.Default.UniteDe(v).Should().Be("H");
    }

    [Theory]
    [InlineData("H", "25.5")]
    [InlineData("J", "3.19")]
    public async Task Other_AbsenceIsWrittenInItsColumn_InTheChosenUnit(string unite, string attendu)
    {
        var (sut, db) = Autre(AvecAbsence(unite));

        await sut.WriteAsync(new BddSage { NomBd = "SAGE_AUTRE", TypeBase = "AUTRE" }, [Ligne], Contexte());

        var absence = db.Updates.Should().ContainSingle(u => u.Sql.Contains("[JoursAbsence]")).Which;
        absence.Value.Should().Be(decimal.Parse(attendu, System.Globalization.CultureInfo.InvariantCulture));
        absence.Matricule.Should().Be("MAT01");
        db.Updates.Should().HaveCount(8);
    }

    [Fact]
    public async Task Other_CheckedAbsenceWithoutDestination_IsRefusedBeforeAnyWrite()
    {
        var (sut, db) = Autre(AvecAbsence("J"), avecDestination: false);

        var act = () => sut.WriteAsync(new BddSage { NomBd = "SAGE_AUTRE", TypeBase = "AUTRE" }, [Ligne], Contexte());

        await act.Should().ThrowAsync<DomainException>().WithMessage("*la catégorie Absence n'est pas configurée*");
        db.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Other_UncheckedAbsence_IsNeverWritten()
    {
        var (sut, db) = Autre(PayrollVariableSelection.Default);

        await sut.WriteAsync(new BddSage { NomBd = "SAGE_AUTRE", TypeBase = "AUTRE" }, [Ligne], Contexte());

        db.Updates.Should().HaveCount(7).And.NotContain(u => u.Sql.Contains("[JoursAbsence]"));
    }

    [Fact]
    public async Task Other_ColumnWithOneDecimal_RefusesADaysValueWithTwoDecimals()
    {
        var (sut, db) = Autre(AvecAbsence("J"), colonne: new OtherPayrollColumn("decimal", 9, 1));

        var act = () => sut.WriteAsync(new BddSage { NomBd = "SAGE_AUTRE", TypeBase = "AUTRE" }, [Ligne], Contexte());

        await act.Should().ThrowAsync<DomainException>().WithMessage($"La valeur {3.19m} du matricule MAT01 n'est pas compatible avec la colonne JoursAbsence."); // même mise en forme (culture courante) que le message du code, comme pour Retard
        db.Updates.Should().BeEmpty();
    }

    // =====================================================================================================
    // Aides
    // =====================================================================================================

    private sealed record Ctx(PayrollVariableService Sut, InMemoryUnitOfWork Uow, RecordingSendLog Envois,
        Mock<IDatabaseConfigurationAuthorizer> Autorisation, ClosingChecklistCache Checklist);

    private static Ctx Creer(string typeBase = "STANDARD", SageConstantOptionDto[]? codes = null)
    {
        var nom = typeBase == "AUTRE" ? "SAGE_AUTRE" : "SAGE_TEST";
        var uow = new InMemoryUnitOfWork();
        var tenant = new Mock<ITenantConnectionService>();
        tenant.SetupGet(t => t.SageDb).Returns(nom);
        tenant.Setup(t => t.GetSageRowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BddSage { NomBd = nom, TypeBase = typeBase });
        var constantes = new Mock<ICodeConstanteService>();
        constantes.Setup(c => c.GetOvertimeSageCodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new OvertimeSageCodes());
        constantes.Setup(c => c.ListSageConstantOptionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(codes ?? []);
        var schema = new FakePayrollTargetSchema();
        schema.Tables["PAIE_CUMULS"] =
        [
            new("Matricule", "nvarchar", null, null),
            .. PayrollVariableSelection.HeuresSup.Select(v => new PayrollTargetColumn($"Heures{v}", "decimal", 18, 2)),
            new("JoursAbsence", "decimal", 18, 2),
            new("Entier", "int", 10, 0),
            new("Libelle", "nvarchar", null, null),
        ];
        var envois = new RecordingSendLog();
        var autorisation = new Mock<IDatabaseConfigurationAuthorizer>();
        var checklist = new ClosingChecklistCache();
        var sut = new PayrollVariableService(uow, tenant.Object, autorisation.Object, new TestCurrentUser(1, 1), envois,
            constantes.Object, Mock.Of<IExternalDiscoveryService>(), new PayrollVariableCache(), new ClosingAnalysisCache(), checklist,
            new HorlogeFixe(), NullLogger<PayrollVariableService>.Instance, schema);
        return new Ctx(sut, uow, envois, autorisation, checklist);
    }

    /// <summary>Codes d'absence de l'application (extrait de T_CODEABSENCE de la copie, libellés seulement).</summary>
    private static void CodesAbsence(InMemoryUnitOfWork uow) => uow.Get<CodeAbsence>().Items.AddRange([
        new CodeAbsence { IdAbsence = 7, IntituleAbsence = "Abs non payés", NotPay = true },
        new CodeAbsence { IdAbsence = 1029, IntituleAbsence = "MISE A PIED", NotPay = false },
        new CodeAbsence { IdAbsence = 1027, IntituleAbsence = "PERMISSION", NotPay = false },
        new CodeAbsence { IdAbsence = 1031, IntituleAbsence = "ABSENCE NON JUSTIFIEE", NotPay = false },
    ]);

    private static PayrollVariablesSaveRequest Demande(params (string Variable, bool Actif, string? Code, string? Unite)[] changes) =>
        new(PayrollVariableSelection.Toutes.Select(nom =>
        {
            var change = changes.FirstOrDefault(c => c.Variable == nom);
            return change.Variable is null
                ? new PayrollVariableSaveDto(nom, PayrollVariableSelection.Default.EstActive(nom), null, null, null)
                : new PayrollVariableSaveDto(nom, change.Actif, change.Code, change.Unite, null);
        }).ToList(), null);

    private static PayrollSyncContext Contexte() => new(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), "rh", Guid.NewGuid());

    private static (OtherPayrollWriter Sut, FakeOtherPayrollDatabase Db) Autre(
        PayrollVariableSelection selection, bool avecDestination = true, OtherPayrollColumn? colonne = null)
    {
        var journal = new List<string>();
        var db = new FakeOtherPayrollDatabase(journal);
        db.Columns["MatriculeEmploye"] = new OtherPayrollColumn("nvarchar", null, null);
        foreach (var c in PayrollWritingConfiguration.Categories)
            db.Columns[$"Heures{c}"] = new OtherPayrollColumn("decimal", 18, 4);
        db.Columns["JoursAbsence"] = colonne ?? new OtherPayrollColumn("decimal", 18, 2);
        var uow = new InMemoryUnitOfWork();
        uow.Get<PaieEcriture>().Items.AddRange(PayrollWritingConfiguration.Categories.Select(c => new PaieEcriture
        {
            BddSage = "SAGE_AUTRE", TableCible = "PAIE_CUMULS", ColMatricule = "MatriculeEmploye", Categorie = c, ColValeur = $"Heures{c}"
        }));
        if (avecDestination)
            uow.Get<PaieEcriture>().Items.Add(new PaieEcriture
            {
                BddSage = "SAGE_AUTRE", TableCible = "PAIE_CUMULS", ColMatricule = "MatriculeEmploye", Categorie = "Absence", ColValeur = "JoursAbsence"
            });
        var sut = new OtherPayrollWriter(uow, Mock.Of<ITenantConnectionService>(), new CapturingAudit(),
            NullLogger<OtherPayrollWriter>.Instance, new FixedVariableService(selection), new RecordingSendLog(journal),
            _ => Task.FromResult<IOtherPayrollDatabase>(db));
        return (sut, db);
    }

    private sealed class TestStandardWriter(
        ISagePayrollWriter sageWriter, Application.Abstractions.Audit.IAuditTrail auditTrail, IPayrollVariableService? variables = null,
        IPayrollSendLog? sendLog = null)
        : StandardPayrollWriter(sageWriter, null!, null!, null!, auditTrail, variables, sendLog)
    {
        protected override async Task<SagePayrollTarget> GetInputAsync(CancellationToken ct) =>
            Cible(await (variables ?? new FixedVariableService(PayrollVariableSelection.Default)).GetSelectionAsync("SAGE_TEST", ct));
    }

    private sealed class CapturingAudit : Application.Abstractions.Audit.IAuditTrail
    {
        public List<PayrollSyncAudit> Audits { get; } = new();

        public Task WritePayrollSyncAsync(PayrollSyncAudit audit, CancellationToken cancellationToken = default)
        {
            Audits.Add(audit);
            return Task.CompletedTask;
        }
    }

    private sealed class HorlogeFixe : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Maintenant;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
