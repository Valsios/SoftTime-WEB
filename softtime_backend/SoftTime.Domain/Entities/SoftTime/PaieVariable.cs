namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// T_PAIE_VARIABLE : choix, pour une base SAGE, d'une variable envoyée ou non à la paie. Une base sans ligne
/// garde le comportement historique (voir <c>PayrollVariableSelection.Default</c>).
/// </summary>
public class PaieVariable
{
    public int Id { get; set; }

    public string BddSage { get; set; } = string.Empty;

    public string Variable { get; set; } = string.Empty;

    public bool Actif { get; set; }

    /// <summary>Base SAGE : code constante de paie (Retard, Absence ; les heures supplémentaires utilisent T_CODE_CONSTANTE).</summary>
    public string? CodePaie { get; set; }

    /// <summary>« H » (heures) ou « J » (jours) pour Retard et Absence.</summary>
    public string? Unite { get; set; }

    /// <summary>Autre base : colonne cible (Retard, Absence ; les heures supplémentaires utilisent T_PAIE_ECRITURE).</summary>
    public string? ColCible { get; set; }

    public DateTime? ModifieLe { get; set; }

    public string? ModifiePar { get; set; }
}
