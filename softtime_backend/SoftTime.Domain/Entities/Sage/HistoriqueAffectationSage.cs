namespace SoftTime.Domain.Entities.Sage;

public class HistoriqueAffectationSage
{
    public int IdHstAffectation { get; set; }

    public int NumSalarie { get; set; }
    public string? Departement { get; set; }
    public string? Service { get; set; }
    public string? Categorie { get; set; }
    public DateTime? DateDebut { get; set; }
    public DateTime? DateSortiePoste { get; set; }
}
