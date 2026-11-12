using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class WeeklyActivityRulesTests
{
    [Fact]
    public void ASemaineActive_SansHeuresNiPlanning_RetourneFaux()
    {
        // Arrange / Act
        var result = WeeklyActivityRules.ASemaineActive(Array.Empty<HeureCorrigee>(), Array.Empty<PlanningSalarie>());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ASemaineActive_AvecUneHeureCorrigee_RetourneVrai()
    {
        // Arrange
        var heures = new[] { new HeureCorrigee() };

        // Act
        var result = WeeklyActivityRules.ASemaineActive(heures, Array.Empty<PlanningSalarie>());

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ASemaineActive_AvecUnJourPlanifieTravaille_RetourneVrai()
    {
        // Arrange
        var planning = new[] { new PlanningSalarie { OffP = false } };

        // Act
        var result = WeeklyActivityRules.ASemaineActive(Array.Empty<HeureCorrigee>(), planning);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ASemaineActive_AvecSeulementDesJoursOff_RetourneFaux()
    {
        // Arrange
        var planning = new[] { new PlanningSalarie { OffP = true }, new PlanningSalarie { OffP = true } };

        // Act
        var result = WeeklyActivityRules.ASemaineActive(Array.Empty<HeureCorrigee>(), planning);

        // Assert
        result.Should().BeFalse();
    }
}
