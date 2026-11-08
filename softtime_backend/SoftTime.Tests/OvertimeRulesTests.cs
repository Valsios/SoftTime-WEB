using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class OvertimeRulesTests
{
    [Fact]
    public void SplitWeekly_FirstEightHours_Are130()
    {
        var split = OvertimeRules.SplitWeekly(11);
        split.Hs1.Should().Be(8);
        split.Hs2.Should().Be(3);
        split.Hs.Should().Be(11);
    }

    [Fact]
    public void SplitWeekly_Zero_IsEmpty()
    {
        var split = OvertimeRules.SplitWeekly(0);
        split.Hs1.Should().Be(0);
        split.Hs2.Should().Be(0);
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
        (exo.Exo130 + exo.Exo150).Should().Be(20);
        (exo.Impo130 + exo.Impo150 >= 2).Should().BeTrue();
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

        exo.Exo130.Should().Be(16m);
        exo.Exo150.Should().Be(4m);
        exo.Impo130.Should().Be(0m);
        exo.Impo150.Should().Be(6m);
    }

    [Fact]
    public void AllocateExempt_CasE_Synthese_UneSemaineDe26h()
    {
        // 8 h à 130 %, 18 h à 150 % => 20 h exonérées (8 + 12), 6 h imposables à 150 %.
        var split = OvertimeRules.SplitWeekly(26);
        var exo = OvertimeRules.AllocateExempt(new[] { split });

        split.Hs1.Should().Be(8m);
        split.Hs2.Should().Be(18m);
        exo.Exo130.Should().Be(8m);
        exo.Exo150.Should().Be(12m);
        exo.Impo130.Should().Be(0m);
        exo.Impo150.Should().Be(6m);
    }

    [Fact]
    public void AllocateExempt_SansSemaine_RetourneDesZeros()
    {
        var exo = OvertimeRules.AllocateExempt(Array.Empty<HsWeekSplit>());

        exo.Exo130.Should().Be(0m);
        exo.Exo150.Should().Be(0m);
        exo.Impo130.Should().Be(0m);
        exo.Impo150.Should().Be(0m);
    }

    [Fact]
    public void IsMondayToSundayWeek_RequiresExactSpan()
    {
        var monday = new DateTime(2026, 8, 31);
        OvertimeRules.IsMondayToSundayWeek(monday, monday.AddDays(6)).Should().BeTrue();
        OvertimeRules.IsMondayToSundayWeek(monday, monday.AddDays(5)).Should().BeFalse();
        OvertimeRules.IsMondayToSundayWeek(monday.AddDays(1), monday.AddDays(7)).Should().BeFalse();
    }

    [Fact]
    public void GetMinuteSortie_RoundsByTolerance()
    {
        OvertimeRules.GetMinuteSortie(35, 30).Should().Be(30);
        OvertimeRules.GetMinuteSortie(20, 30).Should().Be(0);
        // Multiple exact : le desktop retirait un pas de trop (17:45 → 17:30 avec un pas de 15).
        OvertimeRules.GetMinuteSortie(30, 30).Should().Be(30);
        OvertimeRules.GetMinuteSortie(45, 15).Should().Be(45);
        OvertimeRules.GetMinuteSortie(50, 15).Should().Be(45);
        // Pas nul ou minute nulle : aucune modification.
        OvertimeRules.GetMinuteSortie(59, 0).Should().Be(59);
        OvertimeRules.GetMinuteSortie(0, 15).Should().Be(0);
    }

    [Fact]
    public void IsMaxOneMonth_AcceptsWithinMonth()
    {
        var start = new DateTime(2026, 1, 15);
        OvertimeRules.IsMaxOneMonth(start, start).Should().BeTrue();
        OvertimeRules.IsMaxOneMonth(start, start.AddDays(10)).Should().BeTrue();
        OvertimeRules.IsMaxOneMonth(start, new DateTime(2026, 2, 15)).Should().BeTrue();
    }

    [Fact]
    public void IsMaxOneMonth_RejectsExceedingMonth()
    {
        var start = new DateTime(2026, 1, 15);
        OvertimeRules.IsMaxOneMonth(start, new DateTime(2026, 2, 16)).Should().BeFalse();
        OvertimeRules.IsMaxOneMonth(start, start.AddDays(-1)).Should().BeFalse();
    }
}
