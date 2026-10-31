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
        => Assert.True(PlanningGenerationRules.EstOff(value));

    [Theory]
    [InlineData("08:00")]
    [InlineData("0:00")]
    [InlineData("08:00:00")]
    public void EstOff_IsFalse_ForRealHours(string value)
        => Assert.False(PlanningGenerationRules.EstOff(value));

    [Fact]
    public void TryLundi_AcceptsMonday()
    {
        Assert.True(PlanningGenerationRules.TryLundi("2026-08-31", out var lundi));
        Assert.Equal(Lundi, lundi);
    }

    [Fact]
    public void TryLundi_RejectsAnyOtherWeekday()
    {
        // Le desktop exige un lundi : les autres lignes sont ignorées.
        Assert.False(PlanningGenerationRules.TryLundi("2026-09-02", out _));
    }

    [Fact]
    public void TryLundi_IgnoresTimePart()
    {
        Assert.True(PlanningGenerationRules.TryLundi("2026-08-31 00:00:00", out var lundi));
        Assert.Equal(Lundi, lundi);
        Assert.Equal(TimeSpan.Zero, lundi.TimeOfDay);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lundi")]
    public void TryLundi_RejectsEmptyOrInvalid(string? value)
        => Assert.False(PlanningGenerationRules.TryLundi(value, out _));

    [Fact]
    public void TryHeure_ParsesHourAndMinute()
    {
        Assert.True(PlanningGenerationRules.TryHeure("08:30", out var heure));
        Assert.Equal(new TimeSpan(8, 30, 0), heure);
    }

    [Fact]
    public void TryIntervalle_BuildsSameDayRange()
    {
        Assert.True(PlanningGenerationRules.TryIntervalle(Lundi, "08:00", "17:00", out var ha, out var hd));
        Assert.Equal(Lundi.AddHours(8), ha);
        Assert.Equal(Lundi.AddHours(17), hd);
    }

    [Fact]
    public void TryIntervalle_NightShift_EndsNextDay()
    {
        // Fin < début → poste de nuit : la sortie tombe le lendemain.
        Assert.True(PlanningGenerationRules.TryIntervalle(Lundi, "22:00", "06:00", out var ha, out var hd));
        Assert.Equal(Lundi.AddHours(22), ha);
        Assert.Equal(Lundi.AddDays(1).AddHours(6), hd);
    }

    [Fact]
    public void TryIntervalle_EqualHours_StaySameDay()
    {
        Assert.True(PlanningGenerationRules.TryIntervalle(Lundi, "08:00", "08:00", out var ha, out var hd));
        Assert.Equal(Lundi.AddHours(8), ha);
        Assert.Equal(Lundi.AddHours(8), hd);
    }

    [Fact]
    public void TryIntervalle_ReturnsFalse_OnUnreadableHour()
    {
        Assert.False(PlanningGenerationRules.TryIntervalle(Lundi, "huit heures", "17:00", out var ha, out var hd));
        Assert.Null(ha);
        Assert.Null(hd);
    }

    [Fact]
    public void TryIntervalle_TimeSpan_BuildsSameDayRange()
    {
        Assert.True(PlanningGenerationRules.TryIntervalle(
            Lundi, new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0), out var ha, out var hd));
        Assert.Equal(Lundi.AddHours(8), ha);
        Assert.Equal(Lundi.AddHours(17), hd);
    }

    [Fact]
    public void TryIntervalle_TimeSpan_NightShift_EndsNextDay()
    {
        Assert.True(PlanningGenerationRules.TryIntervalle(
            Lundi, new TimeSpan(17, 0, 0), new TimeSpan(7, 0, 0), out var ha, out var hd));
        Assert.Equal(Lundi.AddHours(17), ha);
        Assert.Equal(Lundi.AddDays(1).AddHours(7), hd);
    }

    [Fact]
    public void TryIntervalle_TimeSpan_EqualHours_StaySameDay()
    {
        Assert.True(PlanningGenerationRules.TryIntervalle(
            Lundi, new TimeSpan(8, 0, 0), new TimeSpan(8, 0, 0), out var ha, out var hd));
        Assert.Equal(Lundi.AddHours(8), ha);
        Assert.Equal(Lundi.AddHours(8), hd);
    }

    [Fact]
    public void TryIntervalle_TimeSpan_MissingHour_ReturnsFalse()
    {
        Assert.False(PlanningGenerationRules.TryIntervalle(
            Lundi, new TimeSpan(8, 0, 0), null, out var ha, out var hd));
        Assert.Null(ha);
        Assert.Null(hd);
    }

    [Fact]
    public void IndiceDebut_CategoryFormat_SkipsTwoCellsPerDay()
    {
        // catégorie ; lundi ; (début ; fin) × 7
        Assert.Equal(2, PlanningGenerationRules.IndiceDebut(0, avecPauseParJour: false));
        Assert.Equal(4, PlanningGenerationRules.IndiceDebut(1, avecPauseParJour: false));
        Assert.Equal(14, PlanningGenerationRules.IndiceDebut(6, avecPauseParJour: false));
    }

    [Fact]
    public void IndiceDebut_MatriculeFormat_SkipsThreeCellsPerDay()
    {
        // matricule ; lundi ; (début ; pause ; fin) × 7
        Assert.Equal(2, PlanningGenerationRules.IndiceDebut(0, avecPauseParJour: true));
        Assert.Equal(5, PlanningGenerationRules.IndiceDebut(1, avecPauseParJour: true));
        Assert.Equal(20, PlanningGenerationRules.IndiceDebut(6, avecPauseParJour: true));
    }

    [Fact]
    public void Cellule_ToleratesShortLines()
    {
        var cells = new[] { "CAT A", "2026-08-31", "08:00" };
        Assert.Equal("08:00", PlanningGenerationRules.Cellule(cells, 2));
        Assert.Null(PlanningGenerationRules.Cellule(cells, 3));
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

        Assert.True(PlanningGenerationRules.TryLundi(PlanningGenerationRules.Cellule(cells, 1), out var lundi));
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
            Assert.True(PlanningGenerationRules.TryIntervalle(
                lundi.AddDays(i), debutTxt, PlanningGenerationRules.Cellule(cells, debutIndex + 1), out var ha, out _));
            jours.Add(ha);
        }

        Assert.Equal(3, offs);
        Assert.Equal(4, jours.Count(j => j != null));
        Assert.Equal(lundi.AddHours(8), jours[0]);
        Assert.Null(jours[2]);
    }
}
