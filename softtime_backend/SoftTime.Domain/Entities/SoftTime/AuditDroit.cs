namespace SoftTime.Domain.Entities.SoftTime;

public class AuditDroit
{
    public int Id { get; set; }

    public DateTime DateAction { get; set; } = DateTime.UtcNow;

    public decimal IdOperateur { get; set; }

    public string LoginOperateur { get; set; } = string.Empty;

    public string TypeCible { get; set; } = string.Empty;

    public decimal IdCible { get; set; }

    public string? NomCible { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? PermsAvant { get; set; }

    public string? PermsApres { get; set; }
}
