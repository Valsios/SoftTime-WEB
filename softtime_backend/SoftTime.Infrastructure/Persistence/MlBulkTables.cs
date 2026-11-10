using System.Data;

using SoftTime.Domain.Entities.Ml;

namespace SoftTime.Infrastructure.Persistence;

/// <summary>Tables en mémoire pour l'insertion en masse (SqlBulkCopy) des jeux de données ML.</summary>
internal static class MlBulkTables
{
    private static readonly (string Name, Type Type, Func<MlSalarieJour, object?> Value)[] DailyColumns =
    {
        ("BddSage", typeof(string), e => e.BddSage),
        ("Matricule", typeof(string), e => e.Matricule),
        ("Date", typeof(DateTime), e => e.Date),
        ("EstJourPlanifie", typeof(bool), e => e.EstJourPlanifie),
        ("EstOff", typeof(bool), e => e.EstOff),
        ("EstFerie", typeof(bool), e => e.EstFerie),
        ("EstWeekend", typeof(bool), e => e.EstWeekend),
        ("EstConge", typeof(bool), e => e.EstConge),
        ("PlanningConnu", typeof(bool), e => e.PlanningConnu),
        ("HeuresPlanifiees", typeof(decimal), e => e.HeuresPlanifiees),
        ("HeuresTravaillees", typeof(decimal), e => e.HeuresTravaillees),
        ("RetardMinutes", typeof(int), e => e.RetardMinutes),
        ("AbsenceNonJustifiee", typeof(bool), e => e.AbsenceNonJustifiee),
        ("AbsenceDeclaree", typeof(bool), e => e.AbsenceDeclaree),
        ("AbsenceHeures", typeof(decimal), e => e.AbsenceHeures),
        ("JourSemaine", typeof(byte), e => e.JourSemaine),
        ("SemaineIso", typeof(byte), e => e.SemaineIso),
        ("AnneeIso", typeof(short), e => e.AnneeIso),
        ("Mois", typeof(byte), e => e.Mois),
        ("JourDuMois", typeof(byte), e => e.JourDuMois),
        ("VeilleFerie", typeof(bool), e => e.VeilleFerie),
        ("LendemainFerie", typeof(bool), e => e.LendemainFerie),
        ("DebutMois", typeof(bool), e => e.DebutMois),
        ("FinMois", typeof(bool), e => e.FinMois),
        ("CategorieId", typeof(int), e => e.CategorieId),
        ("Departement", typeof(string), e => e.Departement),
        ("HeuresSemaineContrat", typeof(decimal), e => e.HeuresSemaineContrat),
        ("MultiPointage", typeof(bool), e => e.MultiPointage),
        ("AncienneteJours", typeof(int), e => e.AncienneteJours),
        ("Absences7j", typeof(int), e => e.Absences7j),
        ("Absences30j", typeof(int), e => e.Absences30j),
        ("Absences90j", typeof(int), e => e.Absences90j),
        ("TauxAbsence30j", typeof(double), e => e.TauxAbsence30j),
        ("Retards30j", typeof(int), e => e.Retards30j),
        ("JoursTravailles7j", typeof(int), e => e.JoursTravailles7j),
        ("JoursDepuisDerniereAbsence", typeof(int), e => e.JoursDepuisDerniereAbsence),
        ("HeuresSupp4Semaines", typeof(decimal), e => e.HeuresSupp4Semaines),
        ("TauxAbsenceEquipe30j", typeof(double), e => e.TauxAbsenceEquipe30j),
        ("EffectifPrevuJour", typeof(int), e => e.EffectifPrevuJour),
        ("CongesPrevusJour", typeof(int), e => e.CongesPrevusJour),
        ("SourceGenereeLe", typeof(DateTime), e => e.SourceGenereeLe),
        ("VersionSchema", typeof(string), e => e.VersionSchema)
    };

    private static readonly (string Name, Type Type, Func<MlSalarieSemaine, object?> Value)[] WeeklyColumns =
    {
        ("BddSage", typeof(string), e => e.BddSage),
        ("Matricule", typeof(string), e => e.Matricule),
        ("SemaineDebut", typeof(DateTime), e => e.SemaineDebut),
        ("SemaineIso", typeof(byte), e => e.SemaineIso),
        ("AnneeIso", typeof(short), e => e.AnneeIso),
        ("HeuresPlanifiees", typeof(decimal), e => e.HeuresPlanifiees),
        ("HeuresTravaillees", typeof(decimal), e => e.HeuresTravaillees),
        ("NbJoursPlanifies", typeof(int), e => e.NbJoursPlanifies),
        ("NbJoursTravailles", typeof(int), e => e.NbJoursTravailles),
        ("CongeJours", typeof(decimal), e => e.CongeJours),
        ("HS", typeof(decimal), e => e.Hs),
        ("HSValidee", typeof(bool), e => e.HsValidee),
        ("Nuit", typeof(decimal), e => e.Nuit),
        ("Dimanche", typeof(decimal), e => e.Dimanche),
        ("Ferie", typeof(decimal), e => e.Ferie),
        ("RetardMinutes", typeof(int), e => e.RetardMinutes),
        ("AbsenceHeures", typeof(decimal), e => e.AbsenceHeures),
        ("HsMoyenne4Semaines", typeof(decimal), e => e.HsMoyenne4Semaines),
        ("HsMoyenne8Semaines", typeof(decimal), e => e.HsMoyenne8Semaines),
        ("Absences4Semaines", typeof(int), e => e.Absences4Semaines),
        ("Retards4Semaines", typeof(int), e => e.Retards4Semaines),
        ("HeuresTravaillees4Semaines", typeof(decimal), e => e.HeuresTravaillees4Semaines),
        ("CategorieId", typeof(int), e => e.CategorieId),
        ("Departement", typeof(string), e => e.Departement),
        ("HeuresSemaineContrat", typeof(decimal), e => e.HeuresSemaineContrat),
        ("MultiPointage", typeof(bool), e => e.MultiPointage),
        ("SourceGenereeLe", typeof(DateTime), e => e.SourceGenereeLe),
        ("VersionSchema", typeof(string), e => e.VersionSchema)
    };

    internal static DataTable BuildDailyTable(IReadOnlyCollection<MlSalarieJour> rows) => Build(DailyColumns, rows);

    internal static DataTable BuildWeeklyTable(IReadOnlyCollection<MlSalarieSemaine> rows) => Build(WeeklyColumns, rows);

    private static object Value(object? value) => value ?? DBNull.Value;

    /// <summary>Une colonne par descripteur, dans l'ordre ; les valeurs nulles deviennent <see cref="DBNull"/>.</summary>
    private static DataTable Build<T>((string Name, Type Type, Func<T, object?> Value)[] columns, IEnumerable<T> rows)
    {
        var table = new DataTable();
        foreach (var column in columns)
            table.Columns.Add(column.Name, column.Type);
        foreach (var entity in rows)
            table.Rows.Add(columns.Select(c => Value(c.Value(entity))).ToArray());
        return table;
    }
}
