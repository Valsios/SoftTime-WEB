namespace SoftTime.Domain.Entities.SoftTime;

public class T_HEUREDIMANCHE
{
    public int ID { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }

    public string? MATRICULE_SAGE { get; set; }

    public DateTime? Date_Dim { get; set; }

    public TimeSpan? HeureDebut { get; set; }

    public TimeSpan? HeureFin { get; set; }

    public decimal? Valeur { get; set; }
}
