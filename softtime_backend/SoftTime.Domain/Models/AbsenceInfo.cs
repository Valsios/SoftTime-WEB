namespace SoftTime.Domain.Models;

public class AbsenceInfo
{
    public string Evenement { get; set; } = string.Empty;
    public string Matricule { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public double Valeur { get; set; }
}
