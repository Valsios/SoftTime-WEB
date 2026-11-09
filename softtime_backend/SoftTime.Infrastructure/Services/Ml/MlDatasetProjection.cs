using SoftTime.Application.Abstractions.Ml;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Models.Ml;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Ml;

internal static class MlDatasetProjection
{

    // ------------------------------------------------------------------ semaines

    internal static List<MlSalarieSemaine> BuildWeekly(DatasetContext ctx, DateTime from, DateTime to)
    {
        var rows = new List<MlSalarieSemaine>();
        foreach (var (cle, days) in ctx.EmployeeDays)
        {
            var semaines = days
                .GroupBy(d => MondayOf(d.Date))
                .OrderBy(g => g.Key)
                .ToList();
            foreach (var semaine in semaines.Where(s => s.Key >= from && s.Key <= to))
            {
                var row = WeekRow(ctx, cle, semaine);
                RenseignerHistorique(row, ctx, cle, semaines);
                rows.Add(row);
            }
        }
        return rows;
    }

    /// <summary>Valeurs de la semaine elle-même : heures, jours et résultat HS (validé s'il existe).</summary>
    private static MlSalarieSemaine WeekRow(DatasetContext ctx, string cle, IGrouping<DateTime, DayRow> semaine)
    {
        var config = ctx.Configs[cle];
        var computation = ctx.WeekResults[(cle, semaine.Key)];
        var jours = semaine.OrderBy(d => d.Date).ToList();
        return new MlSalarieSemaine
        {
            BddSage = ctx.SageDb,
            Matricule = cle,
            SemaineDebut = semaine.Key,
            SemaineIso = (byte)System.Globalization.ISOWeek.GetWeekOfYear(semaine.Key),
            AnneeIso = (short)System.Globalization.ISOWeek.GetYear(semaine.Key),
            HeuresPlanifiees = Math.Round(jours.Sum(d => d.HeuresPlanifiees ?? 0m), 2),
            HeuresTravaillees = Math.Round(jours.Sum(d => d.HeuresTravaillees), 2),
            NbJoursPlanifies = jours.Count(d => d.EstJourPlanifie),
            NbJoursTravailles = jours.Count(d => d.HeuresTravaillees > 0m),
            CongeJours = Math.Round(jours.Sum(d => d.CongeValeur), 1),
            Hs = Math.Round(computation.Hs, 2),
            HsValidee = computation.Validee,
            Nuit = Math.Round(computation.Nuit, 2),
            Dimanche = Math.Round(computation.Dimanche, 2),
            Ferie = Math.Round(computation.Ferie, 2),
            RetardMinutes = (int)Math.Round(computation.Retard),
            AbsenceHeures = Math.Round(computation.Absence, 2),
            CategorieId = config.CategorieId,
            Departement = ctx.CardByKey[cle].Departement,
            HeuresSemaineContrat = config.HeuresSemaine,
            MultiPointage = ctx.MultiPointage,
            SourceGenereeLe = DateTime.Now,
            VersionSchema = MlSchema.Version
        };
    }

    /// <summary>
    /// Historique : semaines complètes et observées avant la semaine cible (pour une semaine future,
    /// les dernières semaines terminées), sur 4 et 8 semaines.
    /// </summary>
    private static void RenseignerHistorique(MlSalarieSemaine row, DatasetContext ctx, string cle, List<IGrouping<DateTime, DayRow>> semaines)
    {
        var ancre = DerniereSemaineConnue(ctx, row.SemaineDebut);
        var precedentes4 = semaines.Where(w => w.Key < ancre && w.Key >= ancre.AddDays(-28)).ToList();
        var precedentes8 = semaines.Where(w => w.Key < ancre && w.Key >= ancre.AddDays(-56)).ToList();
        bool Observe(DayRow d) => d.Date < ctx.ObservedUntil && !ctx.JoursSansPointage.Contains(d.Date);
        decimal HsMoyenne(List<IGrouping<DateTime, DayRow>> source) => source.Count == 0
            ? 0m
            : Math.Round(source.Average(w => ctx.WeekResults[(cle, w.Key)].Hs), 2);

        row.HsMoyenne4Semaines = HsMoyenne(precedentes4);
        row.HsMoyenne8Semaines = HsMoyenne(precedentes8);
        row.Absences4Semaines = precedentes4.Sum(w => w.Count(d => d.AbsenceNonJustifiee && Observe(d)));
        row.Retards4Semaines = precedentes4.Sum(w => w.Count(d => d.RetardMinutes > 0 && Observe(d)));
        row.HeuresTravaillees4Semaines = Math.Round(precedentes4.Sum(w => w.Sum(d => d.HeuresTravaillees)), 2);
    }

    internal static MlSalarieJour ToDailyEntity(DatasetContext ctx, DayRow day)
    {
        var config = ctx.Configs[day.Matricule];
        var card = ctx.CardByKey[day.Matricule];
        var entity = new MlSalarieJour
        {
            BddSage = ctx.SageDb,
            Matricule = day.Matricule,
            Date = day.Date,
            EstJourPlanifie = day.EstJourPlanifie,
            EstOff = day.Off,
            EstFerie = day.Ferie,
            EstWeekend = day.Weekend,
            EstConge = day.Conge,
            PlanningConnu = day.PlanningConnu,
            HeuresPlanifiees = day.HeuresPlanifiees,
            HeuresTravaillees = day.HeuresTravaillees,
            RetardMinutes = day.RetardMinutes,
            AbsenceNonJustifiee = day.AbsenceNonJustifiee,
            AbsenceDeclaree = day.AbsenceDeclaree,
            AbsenceHeures = day.AbsenceHeures,
            CategorieId = config.CategorieId,
            Departement = card.Departement,
            HeuresSemaineContrat = config.HeuresSemaine,
            MultiPointage = ctx.MultiPointage,
            AncienneteJours = card.Date.HasValue ? (day.Date - card.Date.Value.Date).Days : null,
            SourceGenereeLe = DateTime.Now,
            VersionSchema = MlSchema.Version
        };
        RenseignerCalendrier(entity, ctx.Feries, day.Date);
        RenseignerHistorique(entity, day);
        return entity;
    }

    /// <summary>Position du jour dans la semaine, le mois et l'année ISO, et proximité d'un férié.</summary>
    private static void RenseignerCalendrier(MlSalarieJour entity, IReadOnlySet<DateTime> feries, DateTime date)
    {
        entity.JourSemaine = (byte)date.DayOfWeek;
        entity.SemaineIso = (byte)System.Globalization.ISOWeek.GetWeekOfYear(date);
        entity.AnneeIso = (short)System.Globalization.ISOWeek.GetYear(date);
        entity.Mois = (byte)date.Month;
        entity.JourDuMois = (byte)date.Day;
        entity.VeilleFerie = feries.Contains(date.AddDays(1));
        entity.LendemainFerie = feries.Contains(date.AddDays(-1));
        entity.DebutMois = date.Day <= 3;
        entity.FinMois = date.Day >= DateTime.DaysInMonth(date.Year, date.Month) - 2;
    }

    /// <summary>Features glissantes du salarié et de son équipe, déjà calculées sur la journée.</summary>
    private static void RenseignerHistorique(MlSalarieJour entity, DayRow day)
    {
        entity.Absences7j = day.Absences7j;
        entity.Absences30j = day.Absences30j;
        entity.Absences90j = day.Absences90j;
        entity.TauxAbsence30j = day.TauxAbsence30j;
        entity.Retards30j = day.Retards30j;
        entity.JoursTravailles7j = day.JoursTravailles7j;
        entity.JoursDepuisDerniereAbsence = day.JoursDepuisDerniereAbsence;
        entity.HeuresSupp4Semaines = day.HeuresSupp4Semaines;
        entity.TauxAbsenceEquipe30j = day.TauxAbsenceEquipe30j;
        entity.EffectifPrevuJour = day.EffectifPrevuJour;
        entity.CongesPrevusJour = day.CongesPrevusJour;
    }

    // ------------------------------------------------------------------ utilitaires

    internal static string MlKey(string? matricule) => MatriculeIdentity.Cle(matricule);

    internal static DateTime MondayOf(DateTime date)
        => date.Date.AddDays(-(((int)date.Date.DayOfWeek + 6) % 7));

    /// <summary>
    /// Lundi à partir duquel compter les semaines d'historique : celui de la date cible, ou celui
    /// de la semaine en cours si la cible est future (les semaines précédentes sont complètes).
    /// </summary>
    internal static DateTime DerniereSemaineConnue(DatasetContext ctx, DateTime date)
    {
        var lundi = MondayOf(date);
        var lundiCourant = MondayOf(ctx.ObservedUntil);
        return lundi < lundiCourant ? lundi : lundiCourant;
    }
}
