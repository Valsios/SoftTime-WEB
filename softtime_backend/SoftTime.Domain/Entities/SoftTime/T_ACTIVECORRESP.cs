namespace SoftTime.Domain.Entities.SoftTime;
public class T_ACTIVECORRESP
{
    public int ID { get; set; }
    public bool? Active { get; set; }
    public string? SAGE_SERVEUR { get; set; }
    public string? SAGE_BDD { get; set; }
    public string? POINTEUSE_SERVEUR { get; set; }
    public string? POINTEUSE_BDD { get; set; }
    public DateTime? DERNIERE_SYNC { get; set; }
    public DateTime? DATE_MODIF { get; set; }
}