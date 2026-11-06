namespace SoftTime.Infrastructure.Services.PunchProcessing;

/// <summary>Valeurs portées par une anomalie de pointage.</summary>
internal readonly record struct PunchAnomalyValues(
    TimeSpan? H1, TimeSpan? H2, bool? AbsenceAm, bool? AbsencePm, bool EstFerie)
{
    /// <summary>Jour travaillé sans aucun pointage.</summary>
    public static PunchAnomalyValues Absence => new(null, null, true, true, false);

    /// <summary>Pointages non appariables : premier et dernier horaire relevés.</summary>
    public static PunchAnomalyValues Heures(TimeSpan h1, TimeSpan h2, bool estFerie) =>
        new(h1, h2, null, null, estFerie);
}
