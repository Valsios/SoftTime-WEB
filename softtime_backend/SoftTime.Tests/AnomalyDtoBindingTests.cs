using System.Text.Json;

using SoftTime.Application.DTOs;
using SoftTime.Application.Serialization;

namespace SoftTime.Tests;

/// <summary>
/// Contrat JSON des endpoints d'arbitrage : le front envoie les heures d'un &lt;input type="time"&gt;
/// (format « HH:mm », parfois « HH:mm:ss »). Ces tests utilisent la MÊME configuration JSON
/// que l'API (<see cref="SoftTimeJsonOptions" />) afin de ne pas diverger.
/// </summary>
public class AnomalyDtoBindingTests
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        SoftTimeJsonOptions.Configure(options);
        return options;
    }

    [Fact]
    public void AnomalyUpdateDto_BindsHtmlTimeInput_WithoutSeconds()
    {
        // Act
        var dto = JsonSerializer.Deserialize<AnomalyUpdateDto>(
            """{"hEntree":"08:00","hPs":"12:00","hPe":"13:00","hSortie":"17:00"}""", Options);

        // Assert
        dto.Should().NotBeNull();
        dto!.HEntree.Should().Be(new TimeSpan(8, 0, 0));
        dto.HPs.Should().Be(new TimeSpan(12, 0, 0));
        dto.HPe.Should().Be(new TimeSpan(13, 0, 0));
        dto.HSortie.Should().Be(new TimeSpan(17, 0, 0));
    }

    [Fact]
    public void AnomalyUpdateDto_BindsHtmlTimeInput_WithSeconds()
    {
        // Act
        var dto = JsonSerializer.Deserialize<AnomalyUpdateDto>(
            """{"hEntree":"08:00:00","hSortie":"17:00:00"}""", Options);

        // Assert
        dto.Should().NotBeNull();
        dto!.HEntree.Should().Be(new TimeSpan(8, 0, 0));
        dto.HSortie.Should().Be(new TimeSpan(17, 0, 0));
    }

    [Fact]
    public void AnomalyUpdateDto_BindsFlagsAndDates()
    {
        // Act
        var dto = JsonSerializer.Deserialize<AnomalyUpdateDto>(
            """{"dateIn":"2026-08-31","dateOut":"2026-08-31","absAm":true,"feriesPm":true,"intituleAbsence":"Maladie"}""",
            Options);

        // Assert
        dto.Should().NotBeNull();
        dto!.DateIn.Should().Be(new DateTime(2026, 8, 31));
        dto.AbsAm.Should().BeTrue();
        dto.FeriesPm.Should().BeTrue();
        dto.IntituleAbsence.Should().Be("Maladie");
        dto.AbsPm.Should().BeNull();
    }

    [Fact]
    public void AnomalyValidationRequest_BindsIds()
    {
        // Act
        var request = JsonSerializer.Deserialize<AnomalyValidationRequest>("""{"ids":[1,2,3]}""", Options);

        // Assert
        request.Should().NotBeNull();
        request!.Ids.Should().Equal(new List<decimal> { 1, 2, 3 });
    }
}
