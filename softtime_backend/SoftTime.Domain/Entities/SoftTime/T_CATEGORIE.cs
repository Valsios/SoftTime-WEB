namespace SoftTime.Domain.Entities.SoftTime;

public class T_CATEGORIE
{
    public int IDCATEGORIE { get; set; }

    public string? INTITULE { get; set; }

    public int ID_CARDPAIE { get; set; }

    public TimeSpan? PAUSE { get; set; }

    public decimal? HEURESEMAINE { get; set; }

    public string? BDD_SAGE { get; set; }

    public string? BDD_POINTEUSE { get; set; }
}
