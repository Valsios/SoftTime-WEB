using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class OvertimeRulesTests
{
    [Fact]
    public void SplitWeekly_FirstEightHours_Are130()
    {
        var split = OvertimeRules.SplitWeekly(11);
        Assert.Equal(8, split.Hs1);
        Assert.Equal(3, split.Hs2);
        Assert.Equal(11, split.Hs);
    }

    [Fact]
    public void SplitWeekly_Zero_IsEmpty()
    {
        var split = OvertimeRules.SplitWeekly(0);
        Assert.Equal(0, split.Hs1);
        Assert.Equal(0, split.Hs2);
    }

    [Fact]
    public void AllocateExempt_CapsAt20Hours()
    {
        var weeks = new List<HsWeekSplit>
        {
            OvertimeRules.SplitWeekly(10),
            OvertimeRules.SplitWeekly(12)
        };
        var exo = OvertimeRules.AllocateExempt(weeks);
        Assert.Equal(20, exo.Exo130 + exo.Exo150);
        Assert.True(exo.Impo130 + exo.Impo150 >= 2);
    }

    [Fact]
    public void AllocateExempt_CasD_Synthese_DeuxSemaines()
    {
        // S1 = 12 h, S2 = 14 h => 20 h exonérées (16 h à 130 %, 4 h à 150 %), 6 h imposables à 150 %.
        var exo = OvertimeRules.AllocateExempt(new[]
        {
            OvertimeRules.SplitWeekly(12),
            OvertimeRules.SplitWeekly(14)
        });

        Assert.Equal(16m, exo.Exo130);
        Assert.Equal(4m, exo.Exo150);
        Assert.Equal(0m, exo.Impo130);
        Assert.Equal(6m, exo.Impo150);
    }

    [Fact]
    public void AllocateExempt_CasE_Synthese_UneSemaineDe26h()
    {
        // 8 h à 130 %, 18 h à 150 % => 20 h exonérées (8 + 12), 6 h imposables à 150 %.
        var split = OvertimeRules.SplitWeekly(26);
        var exo = OvertimeRules.AllocateExempt(new[] { split });

        Assert.Equal(8m, split.Hs1);
        Assert.Equal(18m, split.Hs2);
        Assert.Equal(8m, exo.Exo130);
        Assert.Equal(12m, exo.Exo150);
        Assert.Equal(0m, exo.Impo130);
        Assert.Equal(6m, exo.Impo150);
    }

    [Fact]
    public void AllocateExempt_SansSemaine_RetourneDesZeros()
    {
        var exo = OvertimeRules.AllocateExempt(Array.Empty<HsWeekSplit>());

        Assert.Equal(0m, exo.Exo130);
        Assert.Equal(0m, exo.Exo150);
        Assert.Equal(0m, exo.Impo130);
        Assert.Equal(0m, exo.Impo150);
    }

    [Fact]
    public void IsMondayToSundayWeek_RequiresExactSpan()
    {
        var monday = new DateTime(2026, 8, 31);
        Assert.True(OvertimeRules.IsMondayToSundayWeek(monday, monday.AddDays(6)));
        Assert.False(OvertimeRules.IsMondayToSundayWeek(monday, monday.AddDays(5)));
        Assert.False(OvertimeRules.IsMondayToSundayWeek(monday.AddDays(1), monday.AddDays(7)));
    }

    [Fact]
    public void GetMinuteSortie_RoundsByTolerance()
    {
        Assert.Equal(30, OvertimeRules.GetMinuteSortie(35, 30));
        Assert.Equal(0, OvertimeRules.GetMinuteSortie(20, 30));
        // Multiple exact : le desktop retirait un pas de trop (17:45 → 17:30 avec un pas de 15).
        Assert.Equal(30, OvertimeRules.GetMinuteSortie(30, 30));
        Assert.Equal(45, OvertimeRules.GetMinuteSortie(45, 15));
        Assert.Equal(45, OvertimeRules.GetMinuteSortie(50, 15));
        // Pas nul ou minute nulle : aucune modification.
        Assert.Equal(59, OvertimeRules.GetMinuteSortie(59, 0));
        Assert.Equal(0, OvertimeRules.GetMinuteSortie(0, 15));
    }

    [Fact]
    public void IsMaxOneMonth_AcceptsWithinMonth()
    {
        var start = new DateTime(2026, 1, 15);
        Assert.True(OvertimeRules.IsMaxOneMonth(start, start));
        Assert.True(OvertimeRules.IsMaxOneMonth(start, start.AddDays(10)));
        Assert.True(OvertimeRules.IsMaxOneMonth(start, new DateTime(2026, 2, 15)));
    }

    [Fact]
    public void IsMaxOneMonth_RejectsExceedingMonth()
    {
        var start = new DateTime(2026, 1, 15);
        Assert.False(OvertimeRules.IsMaxOneMonth(start, new DateTime(2026, 2, 16)));
        Assert.False(OvertimeRules.IsMaxOneMonth(start, start.AddDays(-1)));
    }
}
