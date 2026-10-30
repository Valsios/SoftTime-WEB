using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.Ml;

/// <summary>
/// Registre des modèles entraînés. Un seul modèle actif par société et par type
/// (<see cref="MlModelTypes"/>), garanti par un index filtré côté base. Le binaire ML.NET est
/// stocké en base pour que le déploiement reste multi-instance et sans partage de fichiers.
/// </summary>
[Table("Modele", Schema = "ml")]
public class T_ML_MODELE
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string BddSage { get; set; } = string.Empty;

    /// <summary><c>Absence</c>, <c>HsProbabilite</c> ou <c>HsMontant</c>.</summary>
    public string TypeModele { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;
    public string Algorithme { get; set; } = string.Empty;

    /// <summary><c>Active</c>, <c>Archive</c>, <c>Rejete</c> ou <c>Echec</c>.</summary>
    public string Statut { get; set; } = MlModelStatus.Rejete;

    public bool EstActif { get; set; }
    public DateTime EntraineLe { get; set; }
    public DateTime PeriodeDebut { get; set; }
    public DateTime PeriodeFin { get; set; }
    public int NbLignes { get; set; }
    public int? NbPositifs { get; set; }

    /// <summary>Métriques sérialisées (AUC, PR-AUC, Brier, MAE, RMSE, R², baseline…).</summary>
    public string MetriquesJson { get; set; } = "{}";

    /// <summary>SHA-256 du contrat de features : un modèle dont le hash diffère est ignoré.</summary>
    public string SchemaHash { get; set; } = string.Empty;

    public byte[]? ModeleBinaire { get; set; }
    public string? Commentaire { get; set; }
}

public static class MlModelStatus
{
    public const string Active = "Active";
    public const string Archive = "Archive";
    public const string Rejete = "Rejete";
    public const string Echec = "Echec";
}
