namespace SoftTime.Domain.Entities.SoftTime;
public class CardPaie
{
    public int Id { get; set; }
    public string? SageMatricule { get; set; }
    public string? SageNom { get; set; }
    public string? SagePrenom { get; set; }
    public string? PointeuseNumero { get; set; }
    public string? PointeuseNom { get; set; }
    public string? SageServeur { get; set; }
    public string? PointeuseServeur { get; set; }
    public string? SageBdd { get; set; }
    public string? PointeuseBdd { get; set; }
    public DateTime? Date { get; set; }
    public string Origine { get; set; } = "AUTO";
    public bool Actif { get; set; } = true;
    public string? Departement { get; set; }
}