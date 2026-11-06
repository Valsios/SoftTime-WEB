namespace SoftTime.Domain.Entities.SoftTime;

public class T_CANTINE
{
    public int IDCANTINE { get; set; }

    public string? NOM_BDD_SAGE { get; set; }

    public string? NOM_BDD_POINTEUSE { get; set; }

    public string? MATRICULE_SAGE { get; set; }

    public DateTime? DATEDEB { get; set; }

    public DateTime? DATEFIN { get; set; }

    public int? TOTAL_CANTINE { get; set; }
}
