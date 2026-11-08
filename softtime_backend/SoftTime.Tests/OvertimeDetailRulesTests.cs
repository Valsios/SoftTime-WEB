using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Indicateurs journaliers du détail des heures supplémentaires (portage de <c>DetailHS</c>).
/// Le décompte de nuit (22:00 → 05:00, arbitré par les cotations) est couvert par
/// <see cref="OvertimeCalculatorTests"/>.
/// </summary>
public class OvertimeDetailRulesTests
{
    [Fact]
    public void HeuresSupJournalieres_KeepsTheDesktopEightHourThreshold()
    {
        // Desktop : Math.Max(0, totalHeures - 8).
        OvertimeRules.HeuresSupJournalieres(new TimeSpan(8, 0, 0)).Should().Be(0m);
        OvertimeRules.HeuresSupJournalieres(new TimeSpan(6, 30, 0)).Should().Be(0m);
        OvertimeRules.HeuresSupJournalieres(new TimeSpan(9, 30, 0)).Should().Be(1.5m);
    }

    [Fact]
    public void HeuresSupJournalieres_AcceptsAnExplicitThreshold()
        => OvertimeRules.HeuresSupJournalieres(new TimeSpan(10, 0, 0), seuil: 8).Should().Be(2m);

    [Fact]
    public void HeuresSupJournalieres_NeverReturnsNegative()
        => OvertimeRules.HeuresSupJournalieres(TimeSpan.Zero).Should().Be(0m);
}
