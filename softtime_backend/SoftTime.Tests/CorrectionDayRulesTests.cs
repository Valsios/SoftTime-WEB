using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class CorrectionDayRulesTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);

    private static PunchPair P(DateTime date, int hEntree, int hSortie)
        => new(date, new TimeSpan(hEntree, 0, 0), date, new TimeSpan(hSortie, 0, 0));

    [Fact]
    public void DeuxPaires_SommeLesDureesSansRetrancherLaPause()
    {
        var paires = new[] { P(Lundi, 8, 12), P(Lundi, 13, 17) };

        var synthese = CorrectionDayRules.Summarize(paires, TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.FromHours(8), synthese.HeuresTravaillees);
        Assert.Equal(new TimeSpan(8, 0, 0), synthese.Entree);
        Assert.Equal(new TimeSpan(17, 0, 0), synthese.Sortie);
        Assert.Equal(2, synthese.NombrePaires);
    }

    [Fact]
    public void UneSeulePaire_RetrancheLaPause()
    {
        var paires = new[] { P(Lundi, 8, 17) };

        var synthese = CorrectionDayRules.Summarize(paires, TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.FromHours(8), synthese.HeuresTravaillees);
    }

    [Fact]
    public void Nuit_CompteLesHeuresDeNuit()
    {
        var mardi = Lundi.AddDays(1);
        var paires = new[]
        {
            new PunchPair(Lundi, new TimeSpan(22, 0, 0), mardi, new TimeSpan(6, 0, 0))
        };

        var synthese = CorrectionDayRules.Summarize(paires);

        Assert.Equal(TimeSpan.FromHours(8), synthese.HeuresTravaillees);
        Assert.Equal(TimeSpan.FromHours(8), synthese.HeuresNuit);
        Assert.Equal(new TimeSpan(22, 0, 0), synthese.Entree);
        Assert.Equal(new TimeSpan(6, 0, 0), synthese.Sortie);
    }

    [Fact]
    public void AucunePaire_RetourneUneSyntheseVide()
    {
        var synthese = CorrectionDayRules.Summarize(Array.Empty<PunchPair>(), TimeSpan.FromHours(1));

        Assert.Null(synthese.Entree);
        Assert.Null(synthese.Sortie);
        Assert.Equal(TimeSpan.Zero, synthese.HeuresTravaillees);
        Assert.Equal(0, synthese.NombrePaires);
    }
}
