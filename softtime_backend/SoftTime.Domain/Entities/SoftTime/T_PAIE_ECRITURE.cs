namespace SoftTime.Domain.Entities.SoftTime;

public class T_PAIE_ECRITURE
{
    public int ID { get; set; }

    public string BDD_SAGE { get; set; } = string.Empty;

    public string TABLE_CIBLE { get; set; } = string.Empty;

    public string COL_MATRICULE { get; set; } = string.Empty;

    public string CATEGORIE { get; set; } = string.Empty;

    public string COL_VALEUR { get; set; } = string.Empty;
}
