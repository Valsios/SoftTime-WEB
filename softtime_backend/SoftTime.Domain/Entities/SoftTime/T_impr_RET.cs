namespace SoftTime.Domain.Entities.SoftTime;

public class T_impr_RET
{
    public int ID { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }

    public string? MATRICULES { get; set; }

    public string? DATE_POINTAGE { get; set; }

    public TimeSpan? HEURE_ENTREE { get; set; }

    public TimeSpan? RETARDS { get; set; }
}
