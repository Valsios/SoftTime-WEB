using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Qualification d'une journée : fin de vacation normalisée, détection des postes de nuit et
/// règle d'absence partagée par la validation semaine et le tableau de bord.
/// </summary>
public class JourneeRulesTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);

    [Fact]
    public void NormaliserFin_MemeJourInchange()
    {
        var fin = JourneeRules.NormaliserFin(Lundi.AddHours(8), Lundi.AddHours(17));
        Assert.Equal(Lundi.AddHours(17), fin);
    }

    [Fact]
    public void NormaliserFin_FinAvantDebut_PasseAuLendemain()
    {
        var fin = JourneeRules.NormaliserFin(Lundi.AddHours(22), Lundi.AddHours(6));
        Assert.Equal(Lundi.AddDays(1).AddHours(6), fin);
    }

    [Fact]
    public void NormaliserFin_SansPlanning_ResteNulle()
        => Assert.Null(JourneeRules.NormaliserFin(null, Lundi.AddHours(17)));

    [Theory]
    [InlineData(22, 6)]   // saisie manuelle : fin le même jour
    [InlineData(6, 22)]   // générateur CSV : fin stockée le lendemain
    public void EstNuit_DetecteLesDeuxConventions(int heureDebut, int heureFin)
    {
        var debut = Lundi.AddHours(heureDebut);
        var fin = heureDebut == 22 ? Lundi.AddHours(heureFin) : Lundi.AddDays(1).AddHours(heureFin);
        Assert.True(JourneeRules.EstNuit(debut, fin));
    }

    [Fact]
    public void EstNuit_RefuseUneJourneeDeJour()
        => Assert.False(JourneeRules.EstNuit(Lundi.AddHours(8), Lundi.AddHours(17)));

    [Fact]
    public void EstNuit_SansSortie_ResteFaux()
        => Assert.False(JourneeRules.EstNuit(Lundi.AddHours(8), null));

    [Fact]
    public void EstAbsence_JourNonPlanifie_NEstPasUneAbsence()
        => Assert.False(JourneeRules.EstAbsence(false, false, false, false, modeStrict: false));

    [Fact]
    public void EstAbsence_JourDeConge_NEstPasUneAbsence()
        => Assert.False(JourneeRules.EstAbsence(true, true, false, false, modeStrict: false));

    [Fact]
    public void EstAbsence_JourTravaille_NEstPasUneAbsence()
        => Assert.False(JourneeRules.EstAbsence(true, false, aCorrection: true, aAnomalie: false, modeStrict: false));

    [Fact]
    public void EstAbsence_JourPlanifieSansPointage_EstUneAbsence()
        => Assert.True(JourneeRules.EstAbsence(true, false, false, false, modeStrict: false));

    [Fact]
    public void EstAbsence_ModeStrict_JourEnAttenteDArbitrageNEstPasUneAbsence()
        => Assert.False(JourneeRules.EstAbsence(true, false, false, aAnomalie: true, modeStrict: true));

    [Fact]
    public void EstAbsence_ModeHistorique_JourEnAttenteDArbitrageResteUneAbsence()
        => Assert.True(JourneeRules.EstAbsence(true, false, false, aAnomalie: true, modeStrict: false));

    // ---- Hors planning ----

    [Fact]
    public void EstHorsPlanning_JourPlanifie_Non()
        => Assert.False(JourneeRules.EstHorsPlanning(Lundi, aLignePlanning: true, ligneOff: false,
            carteAPlanning: true, signalerLesTrous: false));

    [Fact]
    public void EstHorsPlanning_SansLigneDePlanning_ResteTraiteCommeAvant()
        => Assert.False(JourneeRules.EstHorsPlanning(Lundi, aLignePlanning: false, ligneOff: false,
            carteAPlanning: true, signalerLesTrous: false));

    [Fact]
    public void EstHorsPlanning_TrouDePlanningSignale_SiOptionActive()
        => Assert.True(JourneeRules.EstHorsPlanning(Lundi, aLignePlanning: false, ligneOff: false,
            carteAPlanning: true, signalerLesTrous: true));

    [Fact]
    public void EstHorsPlanning_SalarieSansAucunPlanning_NonSignaleMemeAvecOption()
        => Assert.False(JourneeRules.EstHorsPlanning(Lundi, aLignePlanning: false, ligneOff: false,
            carteAPlanning: false, signalerLesTrous: true));

    [Fact]
    public void EstHorsPlanning_WeekEnd_ToujoursHorsPlanning()
    {
        var samedi = Lundi.AddDays(5);
        var dimanche = Lundi.AddDays(6);
        Assert.True(JourneeRules.EstHorsPlanning(samedi, true, false, true, false));
        Assert.True(JourneeRules.EstHorsPlanning(dimanche, true, false, true, false));
    }

    [Fact]
    public void EstHorsPlanning_JourOff_ToujoursHorsPlanning()
        => Assert.True(JourneeRules.EstHorsPlanning(Lundi, aLignePlanning: true, ligneOff: true,
            carteAPlanning: true, signalerLesTrous: false));
}
