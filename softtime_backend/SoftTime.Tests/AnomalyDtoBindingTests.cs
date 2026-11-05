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
        var dto = JsonSerializer.Deserialize<AnomalyUpdateDto>(
            """{"hEntree":"08:00","hPs":"12:00","hPe":"13:00","hSortie":"17:00"}""", Options);

        Assert.NotNull(dto);
        Assert.Equal(new TimeSpan(8, 0, 0), dto!.HEntree);
        Assert.Equal(new TimeSpan(12, 0, 0), dto.HPs);
        Assert.Equal(new TimeSpan(13, 0, 0), dto.HPe);
        Assert.Equal(new TimeSpan(17, 0, 0), dto.HSortie);
    }

    [Fact]
    public void AnomalyUpdateDto_BindsHtmlTimeInput_WithSeconds()
    {
        var dto = JsonSerializer.Deserialize<AnomalyUpdateDto>(
            """{"hEntree":"08:00:00","hSortie":"17:00:00"}""", Options);

        Assert.NotNull(dto);
        Assert.Equal(new TimeSpan(8, 0, 0), dto!.HEntree);
        Assert.Equal(new TimeSpan(17, 0, 0), dto.HSortie);
    }

    [Fact]
    public void AnomalyUpdateDto_BindsFlagsAndDates()
    {
        var dto = JsonSerializer.Deserialize<AnomalyUpdateDto>(
            """{"dateIn":"2026-08-31","dateOut":"2026-08-31","absAm":true,"feriesPm":true,"intituleAbsence":"Maladie"}""",
            Options);

        Assert.NotNull(dto);
        Assert.Equal(new DateTime(2026, 8, 31), dto!.DateIn);
        Assert.True(dto.AbsAm);
        Assert.True(dto.FeriesPm);
        Assert.Equal("Maladie", dto.IntituleAbsence);
        Assert.Null(dto.AbsPm);
    }

    [Fact]
    public void AnomalyValidationRequest_BindsIds()
    {
        var request = JsonSerializer.Deserialize<AnomalyValidationRequest>("""{"ids":[1,2,3]}""", Options);

        Assert.NotNull(request);
        Assert.Equal(new List<decimal> { 1, 2, 3 }, request!.Ids);
    }
}
