namespace SoftTime.Domain.Entities.SoftTime;

public class T_AUDIT_DROIT
{
    public int ID { get; set; }

    public DateTime DATE_ACTION { get; set; } = DateTime.UtcNow;

    public decimal ID_OPERATEUR { get; set; }

    public string LOGIN_OPERATEUR { get; set; } = string.Empty;

    public string TYPE_CIBLE { get; set; } = string.Empty;

    public decimal ID_CIBLE { get; set; }

    public string? NOM_CIBLE { get; set; }

    public string ACTION { get; set; } = string.Empty;

    public string? PERMS_AVANT { get; set; }

    public string? PERMS_APRES { get; set; }
}
