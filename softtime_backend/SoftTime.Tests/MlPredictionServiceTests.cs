using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models.Ml;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

/// <summary>
/// Lectures de pilotage HS : totaux par semaine de l'horizon et suivi prévu / réel des semaines
/// terminées, calculés à partir des prédictions stockées et de la base analytique.
/// </summary>
public class MlPredictionServiceTests
{
    private const string Sage = "SAGE_TEST";
    private static readonly DateTime Lundi = MondayOf(DateTime.Today);

    [Fact]
    public async Task L_horizon_totalise_chaque_semaine_et_signale_les_semaines_sans_prevision()
    {
        var store = new FakeMlStore();
        Montant(store, "001", Lundi, 24m, 22m, 26m);  // au-delà du plafond exonéré de 20 h
        Montant(store, "002", Lundi, 20m, 18m, 22m);  // au plafond : pas au-delà
        Montant(store, "001", Lundi.AddDays(7), 2.5m, 0.5m, 4.5m);

        var semaines = await Service(store).GetHsHorizonAsync();

        Assert.Equal(4, semaines.Count);
        Assert.Equal(Lundi, semaines[0].SemaineDebut);
        Assert.Equal(2, semaines[0].Salaries);
        Assert.Equal(1, semaines[0].AuDelaPlafond);
        Assert.Equal(44m, semaines[0].HeuresPrevues);
        Assert.Equal(40m, semaines[0].BorneBasse);
        Assert.Equal(48m, semaines[0].BorneHaute);
        Assert.Equal(2.5m, semaines[1].HeuresPrevues);
        // Planning pas encore saisi : la semaine existe, vide.
        Assert.Equal(0, semaines[3].Salaries);
        Assert.Equal(0m, semaines[3].HeuresPrevues);
    }

    [Fact]
    public async Task Le_suivi_compare_la_prevision_aux_hs_reelles_des_semaines_terminees()
    {
        var store = new FakeMlStore();
        var semainePassee = Lundi.AddDays(-7);
        Montant(store, "001", semainePassee, 4m, 2m, 6m);   // réel 5 : dans la fourchette, écart 1
        Montant(store, "002", semainePassee, 1m, 0m, 2m);   // réel 4 : hors fourchette, écart 3
        Montant(store, "003", semainePassee, 2m, 0m, 4m);   // pas de ligne réelle : ignoré
        Montant(store, "001", Lundi, 9m, 7m, 11m);          // semaine en cours : pas encore comparable
        Reel(store, "001", semainePassee, 5m);
        Reel(store, "002", semainePassee, 4m);

        var suivi = await Service(store).GetHsAccuracyAsync(8);

        var semaine = Assert.Single(suivi);
        Assert.Equal(semainePassee, semaine.SemaineDebut);
        Assert.Equal(2, semaine.Salaries);
        Assert.Equal(5m, semaine.HeuresPrevues);
        Assert.Equal(9m, semaine.HeuresReelles);
        Assert.Equal(2m, semaine.EcartMoyen);
        Assert.Equal(0.5, semaine.Couverture);
    }

    [Fact]
    public async Task Sans_historique_le_suivi_est_vide()
    {
        var suivi = await Service(new FakeMlStore()).GetHsAccuracyAsync(8);

        Assert.Empty(suivi);
    }

    // ------------------------------------------------------------------ fixtures

    private static MlPredictionService Service(IMlStore store)
    {
        var uow = new InMemoryUnitOfWork();
        uow.Get<T_BDD_SAGE>().Items.Add(new T_BDD_SAGE { ID = 1, NOM_BD = Sage });
        var tenant = new TenantConnectionService(
            uow, new FakeCompanyContext(), new FakeCurrentUser(),
            SoftTime.Tests.Http.TestRights.Service(uow), NullLogger<TenantConnectionService>.Instance);
        var source = new FakeExternalSourceService();
        var options = new MlOptions();
        var dataset = new MlDatasetService(
            uow, tenant, new SageLeaveCalendar(tenant, source), source, store,
            Options.Create(options), NullLogger<MlDatasetService>.Instance);

        return new MlPredictionService(
            dataset, store, new FakeMlPredictor(), uow, tenant, Options.Create(options),
            NullLogger<MlPredictionService>.Instance);
    }

    private static void Montant(FakeMlStore store, string matricule, DateTime semaine, decimal valeur, decimal basse, decimal haute)
        => store.Predictions.Add(new T_ML_PREDICTION
        {
            BddSage = Sage,
            Matricule = matricule,
            TypeModele = MlModelTypes.HsMontant,
            Cible = semaine,
            ValeurPrevue = valeur,
            BorneBasse = basse,
            BorneHaute = haute,
            NiveauRisque = 1,
            ModeleId = 1,
            GenereLe = semaine
        });

    private static void Reel(FakeMlStore store, string matricule, DateTime semaine, decimal hs)
        => store.Weekly.Add(new T_ML_SALARIE_SEMAINE
        {
            BddSage = Sage,
            Matricule = matricule,
            SemaineDebut = semaine,
            HS = hs,
            VersionSchema = MlSchema.Version,
            SourceGenereeLe = DateTime.Now
        });

    private static DateTime MondayOf(DateTime date)
        => date.Date.AddDays(-(((int)date.Date.DayOfWeek + 6) % 7));

    /// <summary>Prédicteur inutilisé par les lectures : seules les lignes stockées comptent.</summary>
    private sealed class FakeMlPredictor : IMlPredictor
    {
        public string FeatureSchemaHash => "hash-test";
        public IReadOnlyList<float> PredictAbsence(byte[] model, IReadOnlyList<MlAbsenceSample> samples) => throw new NotSupportedException();
        public IReadOnlyList<float> PredictHsProbability(byte[] model, IReadOnlyList<MlHsSample> samples) => throw new NotSupportedException();
        public IReadOnlyList<float> PredictHsAmount(byte[] model, IReadOnlyList<MlHsSample> samples) => throw new NotSupportedException();
    }
}
