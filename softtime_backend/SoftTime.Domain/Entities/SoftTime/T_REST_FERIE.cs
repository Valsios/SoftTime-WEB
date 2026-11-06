namespace SoftTime.Domain.Entities.SoftTime;

public class T_REST_FERIE
{
    public int ID { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }

    public string? MATRICULE_SAGE { get; set; }

    public DateTime? DATE_DEBUT_SEMAINE { get; set; }

    public decimal? FERIE_DEBUT_SEMAINE { get; set; }

    public bool? ETAT { get; set; }
}
