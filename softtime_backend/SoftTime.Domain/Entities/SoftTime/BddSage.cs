namespace SoftTime.Domain.Entities.SoftTime;
public class BddSage
{
    public int Id { get; set; }
    public string? Serveur { get; set; }
    public string? TLogin { get; set; }
    public string? TMdp { get; set; }
    public bool? TypeAuth { get; set; }
    public string? NomBd { get; set; }
    public string TypeBase { get; set; } = "STANDARD";
    public string? MapTable { get; set; }
    public string? MapColMatricule { get; set; }
    public string? MapColNom { get; set; }
    public string? MapColPrenom { get; set; }
    public string? MapColBadge { get; set; }
    public string? MapColDepartement { get; set; }
    public string? MapColService { get; set; }
    public string? MapColCodeDepartement { get; set; }
    public string? MapTableCodeConstante { get; set; }
    public string? MapColCodeConstante { get; set; }
}
