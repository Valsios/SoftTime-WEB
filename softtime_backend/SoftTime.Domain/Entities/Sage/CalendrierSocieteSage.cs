namespace SoftTime.Domain.Entities.Sage;

public class CalendrierSocieteSage
{
    public int IdGHRCal { get; set; }

    public DateTime? PeriodeDebut { get; set; }
    public DateTime? PeriodeFin { get; set; }
    public byte? EtatJour { get; set; }
    public byte? Matin { get; set; }
    public byte? ApresMidi { get; set; }
}
