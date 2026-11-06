using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Models.Ml;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

internal static class MlDatasetProjection
{

    // ------------------------------------------------------------------ semaines

    internal static List<MlSalarieSemaine> BuildWeekly(DatasetContext ctx, DateTime from, DateTime to)
    {
        var rows = new List<MlSalarieSemaine>();

        foreach (var (cle, days) in ctx.EmployeeDays)
        {
            var card = ctx.CardByKey[cle];
            var config = ctx.Configs[cle];
            var semaines = days
                .GroupBy(d => MondayOf(d.Date))
                .OrderBy(g => g.Key)
                .ToList();

            foreach (var semaine in semaines)
            {
                if (semaine.Key < from || semaine.Key > to)
                    continue;

                var computation = ctx.WeekResults[(cle, semaine.Key)];
                var jours = semaine.OrderBy(d => d.Date).ToList();
                // Historique : semaines complètes et observées avant la semaine cible (pour une
                // semaine future, les dernières semaines terminées).
                var ancre = DerniereSemaineConnue(ctx, semaine.Key);
                var precedentes4 = semaines
                    .Where(w => w.Key < ancre && w.Key >= ancre.AddDays(-28))
                    .ToList();
                var precedentes8 = semaines
                    .Where(w => w.Key < ancre && w.Key >= ancre.AddDays(-56))
                    .ToList();
                bool Observe(DayRow d) => d.Date < ctx.ObservedUntil && !ctx.JoursSansPointage.Contains(d.Date);

                decimal HsMoyenne(List<IGrouping<DateTime, DayRow>> semainesSource)
                    => semainesSource.Count == 0
                        ? 0m
                        : Math.Round(semainesSource.Average(w => ctx.WeekResults[(cle, w.Key)].Hs), 2);

                rows.Add(new MlSalarieSemaine
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
                    HsMoyenne4Semaines = HsMoyenne(precedentes4),
                    HsMoyenne8Semaines = HsMoyenne(precedentes8),
                    Absences4Semaines = precedentes4.Sum(w => w.Count(d => d.AbsenceNonJustifiee && Observe(d))),
                    Retards4Semaines = precedentes4.Sum(w => w.Count(d => d.RetardMinutes > 0 && Observe(d))),
                    HeuresTravaillees4Semaines = Math.Round(precedentes4.Sum(w => w.Sum(d => d.HeuresTravaillees)), 2),
                    CategorieId = config.CategorieId,
                    Departement = card.Departement,
                    HeuresSemaineContrat = config.HeuresSemaine,
                    MultiPointage = ctx.MultiPointage,
                    SourceGenereeLe = DateTime.Now,
                    VersionSchema = MlSchema.Version
                });
            }
        }

        return rows;
    }

    internal static MlSalarieJour ToDailyEntity(DatasetContext ctx, DayRow day)
    {
        var config = ctx.Configs[day.Matricule];
        var card = ctx.CardByKey[day.Matricule];

        return new MlSalarieJour
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
            JourSemaine = (byte)day.Date.DayOfWeek,
            SemaineIso = (byte)System.Globalization.ISOWeek.GetWeekOfYear(day.Date),
            AnneeIso = (short)System.Globalization.ISOWeek.GetYear(day.Date),
            Mois = (byte)day.Date.Month,
            JourDuMois = (byte)day.Date.Day,
            VeilleFerie = ctx.Feries.Contains(day.Date.AddDays(1)),
            LendemainFerie = ctx.Feries.Contains(day.Date.AddDays(-1)),
            DebutMois = day.Date.Day <= 3,
            FinMois = day.Date.Day >= DateTime.DaysInMonth(day.Date.Year, day.Date.Month) - 2,
            CategorieId = config.CategorieId,
            Departement = card.Departement,
            HeuresSemaineContrat = config.HeuresSemaine,
            MultiPointage = ctx.MultiPointage,
            AncienneteJours = card.Date.HasValue ? (day.Date - card.Date.Value.Date).Days : null,
            Absences7j = day.Absences7j,
            Absences30j = day.Absences30j,
            Absences90j = day.Absences90j,
            TauxAbsence30j = day.TauxAbsence30j,
            Retards30j = day.Retards30j,
            JoursTravailles7j = day.JoursTravailles7j,
            JoursDepuisDerniereAbsence = day.JoursDepuisDerniereAbsence,
            HeuresSupp4Semaines = day.HeuresSupp4Semaines,
            TauxAbsenceEquipe30j = day.TauxAbsenceEquipe30j,
            EffectifPrevuJour = day.EffectifPrevuJour,
            CongesPrevusJour = day.CongesPrevusJour,
            SourceGenereeLe = DateTime.Now,
            VersionSchema = MlSchema.Version
        };
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
