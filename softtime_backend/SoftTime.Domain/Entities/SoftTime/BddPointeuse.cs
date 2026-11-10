namespace SoftTime.Domain.Entities.SoftTime;
public class BddPointeuse
{
    public int Id { get; set; }
    public string? Serveur { get; set; }
    public string? TLogin { get; set; }
    public string? TMdp { get; set; }
    public bool? TypeAuth { get; set; }
    public string? NomBd { get; set; }
    public string? TypePointage { get; set; }
    public bool? Active { get; set; }
    public string TypeBase { get; set; } = "STANDARD";
    public string? MapUserTable { get; set; }
    public string? MapUserColId { get; set; }
    public string? MapUserColBadge { get; set; }
    public string? MapUserColSsn { get; set; }
    public string? MapUserColNom { get; set; }
    public string? MapPunchTable { get; set; }
    public string? MapPunchColUserId { get; set; }
    public string? MapPunchColDatetime { get; set; }
    public string? MapPunchColType { get; set; }
}