using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.Ml;

/// <summary>
/// Prédictions générées en lot pour un horizon donné. <see cref="Cible"/> porte le jour (absence)
/// ou le lundi de la semaine (heures supplémentaires). Les lignes sont supprimées puis réinsérées
/// à chaque génération pour rester idempotentes.
/// </summary>
[Table("Prediction", Schema = "ml")]
public class T_ML_PREDICTION
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public string BddSage { get; set; } = string.Empty;
    public string Matricule { get; set; } = string.Empty;
    public string TypeModele { get; set; } = string.Empty;
    public DateTime Cible { get; set; }

    /// <summary>Probabilité (absence ou HS &gt; 0).</summary>
    public double? Probabilite { get; set; }

    /// <summary>Montant prévu (heures supplémentaires).</summary>
    public decimal? ValeurPrevue { get; set; }

    public decimal? BorneBasse { get; set; }
    public decimal? BorneHaute { get; set; }

    /// <summary>1 = faible, 2 = moyen, 3 = élevé.</summary>
    public byte NiveauRisque { get; set; }

    public int ModeleId { get; set; }
    public DateTime GenereLe { get; set; }
}
