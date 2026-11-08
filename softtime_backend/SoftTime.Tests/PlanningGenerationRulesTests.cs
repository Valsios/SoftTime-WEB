using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Règles de génération de planning depuis un CSV (portage de
/// <c>ImportationPlanningService.GenererPlanningParCategorieAsync</c> / <c>GenererPlanningsAsync</c>).
/// </summary>
public class PlanningGenerationRulesTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);

    [Theory]
    [InlineData("OFF")]
    [InlineData("off")]
    [InlineData(" Off ")]
    public void EstOff_RecognizesToken_WhateverTheCase(string value)
        => PlanningGenerationRules.EstOff(value).Should().BeTrue();

    [Theory]
    [InlineData("08:00")]
    [InlineData("0:00")]
    [InlineData("08:00:00")]
    public void EstOff_IsFalse_ForRealHours(string value)
        => PlanningGenerationRules.EstOff(value).Should().BeFalse();

    [Fact]
    public void TryLundi_AcceptsMonday()
    {
        PlanningGenerationRules.TryLundi("2026-08-31", out var lundi).Should().BeTrue();
        lundi.Should().Be(Lundi);
    }

    [Fact]
    public void TryLundi_RejectsAnyOtherWeekday()
    {
        // Le desktop exige un lundi : les autres lignes sont ignorées.
        PlanningGenerationRules.TryLundi("2026-09-02", out _).Should().BeFalse();
    }

    [Fact]
    public void TryLundi_IgnoresTimePart()
    {
        PlanningGenerationRules.TryLundi("2026-08-31 00:00:00", out var lundi).Should().BeTrue();
        lundi.Should().Be(Lundi);
        lundi.TimeOfDay.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lundi")]
    public void TryLundi_RejectsEmptyOrInvalid(string? value)
        => PlanningGenerationRules.TryLundi(value, out _).Should().BeFalse();

    [Fact]
    public void TryHeure_ParsesHourAndMinute()
    {
        PlanningGenerationRules.TryHeure("08:30", out var heure).Should().BeTrue();
        heure.Should().Be(new TimeSpan(8, 30, 0));
    }

    [Fact]
    public void TryIntervalle_BuildsSameDayRange()
    {
        PlanningGenerationRules.TryIntervalle(Lundi, "08:00", "17:00", out var ha, out var hd).Should().BeTrue();
        ha.Should().Be(Lundi.AddHours(8));
        hd.Should().Be(Lundi.AddHours(17));
    }

    [Fact]
    public void TryIntervalle_NightShift_EndsNextDay()
    {
        // Fin < début → poste de nuit : la sortie tombe le lendemain.
        PlanningGenerationRules.TryIntervalle(Lundi, "22:00", "06:00", out var ha, out var hd).Should().BeTrue();
        ha.Should().Be(Lundi.AddHours(22));
        hd.Should().Be(Lundi.AddDays(1).AddHours(6));
    }

    [Fact]
    public void TryIntervalle_EqualHours_StaySameDay()
    {
        PlanningGenerationRules.TryIntervalle(Lundi, "08:00", "08:00", out var ha, out var hd).Should().BeTrue();
        ha.Should().Be(Lundi.AddHours(8));
        hd.Should().Be(Lundi.AddHours(8));
    }

    [Fact]
    public void TryIntervalle_ReturnsFalse_OnUnreadableHour()
    {
        PlanningGenerationRules.TryIntervalle(Lundi, "huit heures", "17:00", out var ha, out var hd).Should().BeFalse();
        ha.Should().BeNull();
        hd.Should().BeNull();
    }

    [Fact]
    public void TryIntervalle_TimeSpan_BuildsSameDayRange()
    {
        PlanningGenerationRules.TryIntervalle(
            Lundi, new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0), out var ha, out var hd).Should().BeTrue();
        ha.Should().Be(Lundi.AddHours(8));
        hd.Should().Be(Lundi.AddHours(17));
    }

    [Fact]
    public void TryIntervalle_TimeSpan_NightShift_EndsNextDay()
    {
        PlanningGenerationRules.TryIntervalle(
            Lundi, new TimeSpan(17, 0, 0), new TimeSpan(7, 0, 0), out var ha, out var hd).Should().BeTrue();
        ha.Should().Be(Lundi.AddHours(17));
        hd.Should().Be(Lundi.AddDays(1).AddHours(7));
    }

    [Fact]
    public void TryIntervalle_TimeSpan_EqualHours_StaySameDay()
    {
        PlanningGenerationRules.TryIntervalle(
            Lundi, new TimeSpan(8, 0, 0), new TimeSpan(8, 0, 0), out var ha, out var hd).Should().BeTrue();
        ha.Should().Be(Lundi.AddHours(8));
        hd.Should().Be(Lundi.AddHours(8));
    }

    [Fact]
    public void TryIntervalle_TimeSpan_MissingHour_ReturnsFalse()
    {
        PlanningGenerationRules.TryIntervalle(
            Lundi, new TimeSpan(8, 0, 0), null, out var ha, out var hd).Should().BeFalse();
        ha.Should().BeNull();
        hd.Should().BeNull();
    }

    [Fact]
    public void IndiceDebut_CategoryFormat_SkipsTwoCellsPerDay()
    {
        // catégorie ; lundi ; (début ; fin) × 7
        PlanningGenerationRules.IndiceDebut(0, avecPauseParJour: false).Should().Be(2);
        PlanningGenerationRules.IndiceDebut(1, avecPauseParJour: false).Should().Be(4);
        PlanningGenerationRules.IndiceDebut(6, avecPauseParJour: false).Should().Be(14);
    }

    [Fact]
    public void IndiceDebut_MatriculeFormat_SkipsThreeCellsPerDay()
    {
        // matricule ; lundi ; (début ; pause ; fin) × 7
        PlanningGenerationRules.IndiceDebut(0, avecPauseParJour: true).Should().Be(2);
        PlanningGenerationRules.IndiceDebut(1, avecPauseParJour: true).Should().Be(5);
        PlanningGenerationRules.IndiceDebut(6, avecPauseParJour: true).Should().Be(20);
    }

    [Fact]
    public void Cellule_ToleratesShortLines()
    {
        var cells = new[] { "CAT A", "2026-08-31", "08:00" };
        PlanningGenerationRules.Cellule(cells, 2).Should().Be("08:00");
        PlanningGenerationRules.Cellule(cells, 3).Should().BeNull();
    }

    [Fact]
    public void CategoryCsv_GeneratesAWholeWeek()
    {
        var cells = new[]
        {
            "CAT A", "2026-08-31",
            "08:00", "17:00",   // lundi
            "08:00", "17:00",   // mardi
            "OFF", "OFF",       // mercredi
            "08:00", "17:00",
            "08:00", "17:00",
            "OFF", "OFF",       // samedi
            "OFF", "OFF"        // dimanche
        };

        PlanningGenerationRules.TryLundi(PlanningGenerationRules.Cellule(cells, 1), out var lundi).Should().BeTrue();
        var jours = new List<DateTime?>();
        var offs = 0;
        for (var i = 0; i < PlanningGenerationRules.JoursParSemaine; i++)
        {
            var debutIndex = PlanningGenerationRules.IndiceDebut(i, avecPauseParJour: false);
            var debutTxt = PlanningGenerationRules.Cellule(cells, debutIndex);
            if (PlanningGenerationRules.EstOff(debutTxt))
            {
                offs++;
                jours.Add(null);
                continue;
            }
            PlanningGenerationRules.TryIntervalle(
                lundi.AddDays(i), debutTxt, PlanningGenerationRules.Cellule(cells, debutIndex + 1), out var ha, out _).Should().BeTrue();
            jours.Add(ha);
        }

        offs.Should().Be(3);
        jours.Count(j => j != null).Should().Be(4);
        jours[0].Should().Be(lundi.AddHours(8));
        jours[2].Should().BeNull();
    }
}
