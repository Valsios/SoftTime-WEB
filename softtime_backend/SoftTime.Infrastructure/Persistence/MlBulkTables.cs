using System.Data;

using SoftTime.Domain.Entities.Ml;

namespace SoftTime.Infrastructure.Persistence;

internal static class MlBulkTables
{

    internal static DataTable BuildDailyTable(IReadOnlyCollection<MlSalarieJour> rows)
    {
        var table = new DataTable();
        table.Columns.Add("BddSage", typeof(string));
        table.Columns.Add("Matricule", typeof(string));
        table.Columns.Add("Date", typeof(DateTime));
        table.Columns.Add("EstJourPlanifie", typeof(bool));
        table.Columns.Add("EstOff", typeof(bool));
        table.Columns.Add("EstFerie", typeof(bool));
        table.Columns.Add("EstWeekend", typeof(bool));
        table.Columns.Add("EstConge", typeof(bool));
        table.Columns.Add("PlanningConnu", typeof(bool));
        table.Columns.Add("HeuresPlanifiees", typeof(decimal));
        table.Columns.Add("HeuresTravaillees", typeof(decimal));
        table.Columns.Add("RetardMinutes", typeof(int));
        table.Columns.Add("AbsenceNonJustifiee", typeof(bool));
        table.Columns.Add("AbsenceDeclaree", typeof(bool));
        table.Columns.Add("AbsenceHeures", typeof(decimal));
        table.Columns.Add("JourSemaine", typeof(byte));
        table.Columns.Add("SemaineIso", typeof(byte));
        table.Columns.Add("AnneeIso", typeof(short));
        table.Columns.Add("Mois", typeof(byte));
        table.Columns.Add("JourDuMois", typeof(byte));
        table.Columns.Add("VeilleFerie", typeof(bool));
        table.Columns.Add("LendemainFerie", typeof(bool));
        table.Columns.Add("DebutMois", typeof(bool));
        table.Columns.Add("FinMois", typeof(bool));
        table.Columns.Add("CategorieId", typeof(int));
        table.Columns.Add("Departement", typeof(string));
        table.Columns.Add("HeuresSemaineContrat", typeof(decimal));
        table.Columns.Add("MultiPointage", typeof(bool));
        table.Columns.Add("AncienneteJours", typeof(int));
        table.Columns.Add("Absences7j", typeof(int));
        table.Columns.Add("Absences30j", typeof(int));
        table.Columns.Add("Absences90j", typeof(int));
        table.Columns.Add("TauxAbsence30j", typeof(double));
        table.Columns.Add("Retards30j", typeof(int));
        table.Columns.Add("JoursTravailles7j", typeof(int));
        table.Columns.Add("JoursDepuisDerniereAbsence", typeof(int));
        table.Columns.Add("HeuresSupp4Semaines", typeof(decimal));
        table.Columns.Add("TauxAbsenceEquipe30j", typeof(double));
        table.Columns.Add("EffectifPrevuJour", typeof(int));
        table.Columns.Add("CongesPrevusJour", typeof(int));
        table.Columns.Add("SourceGenereeLe", typeof(DateTime));
        table.Columns.Add("VersionSchema", typeof(string));

        foreach (var entity in rows)
        {
            var row = table.NewRow();
            row["BddSage"] = entity.BddSage;
            row["Matricule"] = entity.Matricule;
            row["Date"] = entity.Date;
            row["EstJourPlanifie"] = entity.EstJourPlanifie;
            row["EstOff"] = entity.EstOff;
            row["EstFerie"] = entity.EstFerie;
            row["EstWeekend"] = entity.EstWeekend;
            row["EstConge"] = entity.EstConge;
            row["PlanningConnu"] = entity.PlanningConnu;
            row["HeuresPlanifiees"] = Value(entity.HeuresPlanifiees);
            row["HeuresTravaillees"] = Value(entity.HeuresTravaillees);
            row["RetardMinutes"] = Value(entity.RetardMinutes);
            row["AbsenceNonJustifiee"] = entity.AbsenceNonJustifiee;
            row["AbsenceDeclaree"] = entity.AbsenceDeclaree;
            row["AbsenceHeures"] = Value(entity.AbsenceHeures);
            row["JourSemaine"] = entity.JourSemaine;
            row["SemaineIso"] = entity.SemaineIso;
            row["AnneeIso"] = entity.AnneeIso;
            row["Mois"] = entity.Mois;
            row["JourDuMois"] = entity.JourDuMois;
            row["VeilleFerie"] = entity.VeilleFerie;
            row["LendemainFerie"] = entity.LendemainFerie;
            row["DebutMois"] = entity.DebutMois;
            row["FinMois"] = entity.FinMois;
            row["CategorieId"] = Value(entity.CategorieId);
            row["Departement"] = Value(entity.Departement);
            row["HeuresSemaineContrat"] = Value(entity.HeuresSemaineContrat);
            row["MultiPointage"] = entity.MultiPointage;
            row["AncienneteJours"] = Value(entity.AncienneteJours);
            row["Absences7j"] = entity.Absences7j;
            row["Absences30j"] = entity.Absences30j;
            row["Absences90j"] = entity.Absences90j;
            row["TauxAbsence30j"] = entity.TauxAbsence30j;
            row["Retards30j"] = entity.Retards30j;
            row["JoursTravailles7j"] = entity.JoursTravailles7j;
            row["JoursDepuisDerniereAbsence"] = Value(entity.JoursDepuisDerniereAbsence);
            row["HeuresSupp4Semaines"] = entity.HeuresSupp4Semaines;
            row["TauxAbsenceEquipe30j"] = entity.TauxAbsenceEquipe30j;
            row["EffectifPrevuJour"] = entity.EffectifPrevuJour;
            row["CongesPrevusJour"] = entity.CongesPrevusJour;
            row["SourceGenereeLe"] = entity.SourceGenereeLe;
            row["VersionSchema"] = entity.VersionSchema;
            table.Rows.Add(row);
        }

        return table;
    }

    internal static DataTable BuildWeeklyTable(IReadOnlyCollection<MlSalarieSemaine> rows)
    {
        var table = new DataTable();
        table.Columns.Add("BddSage", typeof(string));
        table.Columns.Add("Matricule", typeof(string));
        table.Columns.Add("SemaineDebut", typeof(DateTime));
        table.Columns.Add("SemaineIso", typeof(byte));
        table.Columns.Add("AnneeIso", typeof(short));
        table.Columns.Add("HeuresPlanifiees", typeof(decimal));
        table.Columns.Add("HeuresTravaillees", typeof(decimal));
        table.Columns.Add("NbJoursPlanifies", typeof(int));
        table.Columns.Add("NbJoursTravailles", typeof(int));
        table.Columns.Add("CongeJours", typeof(decimal));
        table.Columns.Add("HS", typeof(decimal));
        table.Columns.Add("HSValidee", typeof(bool));
        table.Columns.Add("Nuit", typeof(decimal));
        table.Columns.Add("Dimanche", typeof(decimal));
        table.Columns.Add("Ferie", typeof(decimal));
        table.Columns.Add("RetardMinutes", typeof(int));
        table.Columns.Add("AbsenceHeures", typeof(decimal));
        table.Columns.Add("HsMoyenne4Semaines", typeof(decimal));
        table.Columns.Add("HsMoyenne8Semaines", typeof(decimal));
        table.Columns.Add("Absences4Semaines", typeof(int));
        table.Columns.Add("Retards4Semaines", typeof(int));
        table.Columns.Add("HeuresTravaillees4Semaines", typeof(decimal));
        table.Columns.Add("CategorieId", typeof(int));
        table.Columns.Add("Departement", typeof(string));
        table.Columns.Add("HeuresSemaineContrat", typeof(decimal));
        table.Columns.Add("MultiPointage", typeof(bool));
        table.Columns.Add("SourceGenereeLe", typeof(DateTime));
        table.Columns.Add("VersionSchema", typeof(string));

        foreach (var entity in rows)
        {
            var row = table.NewRow();
            row["BddSage"] = entity.BddSage;
            row["Matricule"] = entity.Matricule;
            row["SemaineDebut"] = entity.SemaineDebut;
            row["SemaineIso"] = entity.SemaineIso;
            row["AnneeIso"] = entity.AnneeIso;
            row["HeuresPlanifiees"] = entity.HeuresPlanifiees;
            row["HeuresTravaillees"] = entity.HeuresTravaillees;
            row["NbJoursPlanifies"] = entity.NbJoursPlanifies;
            row["NbJoursTravailles"] = entity.NbJoursTravailles;
            row["CongeJours"] = entity.CongeJours;
            row["HS"] = entity.Hs;
            row["HSValidee"] = entity.HsValidee;
            row["Nuit"] = entity.Nuit;
            row["Dimanche"] = entity.Dimanche;
            row["Ferie"] = entity.Ferie;
            row["RetardMinutes"] = entity.RetardMinutes;
            row["AbsenceHeures"] = entity.AbsenceHeures;
            row["HsMoyenne4Semaines"] = entity.HsMoyenne4Semaines;
            row["HsMoyenne8Semaines"] = entity.HsMoyenne8Semaines;
            row["Absences4Semaines"] = entity.Absences4Semaines;
            row["Retards4Semaines"] = entity.Retards4Semaines;
            row["HeuresTravaillees4Semaines"] = entity.HeuresTravaillees4Semaines;
            row["CategorieId"] = Value(entity.CategorieId);
            row["Departement"] = Value(entity.Departement);
            row["HeuresSemaineContrat"] = Value(entity.HeuresSemaineContrat);
            row["MultiPointage"] = entity.MultiPointage;
            row["SourceGenereeLe"] = entity.SourceGenereeLe;
            row["VersionSchema"] = entity.VersionSchema;
            table.Rows.Add(row);
        }

        return table;
    }

    internal static object Value(object? value) => value ?? DBNull.Value;
}
