namespace SoftTime.Infrastructure.Services;

public sealed class DayRow
{
    public string Matricule { get; init; } = string.Empty;
    public DateTime Date { get; init; }
    public string? Departement { get; init; }
    public bool Off { get; init; }
    public bool Ferie { get; init; }
    public bool Weekend { get; init; }
    public bool Conge { get; init; }
    public decimal CongeValeur { get; init; }
    public bool PlanningConnu { get; init; }
    public bool EstJourPlanifie { get; init; }
    public decimal? HeuresPlanifiees { get; init; }
    public decimal HeuresTravaillees { get; init; }
    public int RetardMinutes { get; init; }
    public bool AbsenceNonJustifiee { get; init; }
    public bool AbsenceDeclaree { get; init; }
    public decimal? AbsenceHeures { get; init; }
    public OvertimeDayInput OvertimeDay { get; init; } = null!;

    public int Absences7j { get; set; }
    public int Absences30j { get; set; }
    public int Absences90j { get; set; }
    public double TauxAbsence30j { get; set; }
    public int Retards30j { get; set; }
    public int JoursTravailles7j { get; set; }
    public int? JoursDepuisDerniereAbsence { get; set; }
    public decimal HeuresSupp4Semaines { get; set; }
    public double TauxAbsenceEquipe30j { get; set; }
    public int EffectifPrevuJour { get; set; }
    public int CongesPrevusJour { get; set; }
}
