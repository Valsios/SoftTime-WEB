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
    }
}
