namespace SoftTime.Domain.Entities.SoftTime;

public class UserPermission
{
    public int Id { get; set; }

    public decimal IdResponsable { get; set; }

    public string PermissionCode { get; set; } = string.Empty;

    public bool IsGranted { get; set; } = true;

    /// <summary>
    /// Vrai : écart par rapport au rôle (IsGranted = ajout, faux = retrait). Faux : ancienne copie complète de la
    /// liste, écrite avant la migration 006 et convertie en écarts au démarrage. Toute nouvelle ligne est un écart.
    /// </summary>
    public bool IsDeviation { get; set; } = true;
}
