using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Indicateurs journaliers du détail des heures supplémentaires (portage de <c>DetailHS</c>).
/// </summary>
public class OvertimeDetailRulesTests
{
    [Theory]
    [InlineData(5, 30, 14, 0)]   // entrée avant 6 h → nuit
    [InlineData(8, 0, 21, 0)]    // sortie à 21 h → nuit
    [InlineData(22, 0, 23, 0)]   // sortie après 21 h → nuit
    public void EstPlageNuit_FlagsNightRanges(int hEntree, int mEntree, int hSortie, int mSortie)
        => Assert.True(OvertimeRules.EstPlageNuit(new TimeSpan(hEntree, mEntree, 0), new TimeSpan(hSortie, mSortie, 0)));

    [Theory]
    [InlineData(6, 0, 20, 59)]   // entrée à 6 h et sortie à 20 h 59 → jour
    [InlineData(9, 0, 17, 0)]
    public void EstPlageNuit_LeavesDayRangesAlone(int hEntree, int mEntree, int hSortie, int mSortie)
        => Assert.False(OvertimeRules.EstPlageNuit(new TimeSpan(hEntree, mEntree, 0), new TimeSpan(hSortie, mSortie, 0)));

    [Fact]
    public void HeuresSupJournalieres_KeepsTheDesktopEightHourThreshold()
    {
        // Desktop : Math.Max(0, totalHeures - 8).
        Assert.Equal(0m, OvertimeRules.HeuresSupJournalieres(new TimeSpan(8, 0, 0)));
        Assert.Equal(0m, OvertimeRules.HeuresSupJournalieres(new TimeSpan(6, 30, 0)));
        Assert.Equal(1.5m, OvertimeRules.HeuresSupJournalieres(new TimeSpan(9, 30, 0)));
    }

    [Fact]
    public void HeuresSupJournalieres_AcceptsAnExplicitThreshold()
        => Assert.Equal(2m, OvertimeRules.HeuresSupJournalieres(new TimeSpan(10, 0, 0), seuil: 8));

    [Fact]
    public void HeuresSupJournalieres_NeverReturnsNegative()
        => Assert.Equal(0m, OvertimeRules.HeuresSupJournalieres(TimeSpan.Zero));
}
