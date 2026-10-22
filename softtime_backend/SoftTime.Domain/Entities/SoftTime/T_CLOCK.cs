using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

[Table("T_CLOCK")]
public class T_CLOCK
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int IDPOINT { get; set; }

    public bool? MULTIPOINT { get; set; }

    /// <summary>
    /// Mode simple « fiable » : appariement durci (dédoublonnage + 2 ou 4 pointages), anomalie
    /// motivée pour tout le reste, jour en anomalie ni travaillé ni absent, jour de congé SAGE
    /// sans anomalie. <c>NULL</c> = comportement historique.
    /// </summary>
    public bool? SIMPLE_STRICT { get; set; }

    /// <summary>
    /// Le calcul des heures déduit la pause réellement pointée (<c>HPE − HPS</c>) au lieu de la
    /// pause de la catégorie. <c>NULL</c> = pause de la catégorie (comportement historique).
    /// </summary>
    public bool? SIMPLE_HPSPE { get; set; }

    /// <summary>Durée minimale plausible d'une journée, en minutes. <c>NULL</c> ou 0 = pas de contrôle.</summary>
    public int? DUREE_MIN_JOUR { get; set; }

    /// <summary>Durée maximale plausible d'une journée, en minutes. <c>NULL</c> ou 0 = pas de contrôle.</summary>
    public int? DUREE_MAX_JOUR { get; set; }

    /// <summary>
    /// Restreint les pointages à la base pointeuse active
    /// (<c>NOM_BDD_POINTEUSE = base active OR NULL</c>). <c>NULL</c> = toutes les bases.
    /// </summary>
    public bool? FILTRE_POINTEUSE { get; set; }

    /// <summary>
    /// Un pointage un jour non planifié (week-end, <c>OffP</c>, absence de planning) produit une
    /// anomalie <c>HORS_PLANNING</c> au lieu d'être ignoré. <c>NULL</c> = ignoré.
    /// </summary>
    public bool? ANOMALIE_HORS_PLANNING { get; set; }

    /// <summary>
    /// Regroupement des pointages par vacation pour les postes de nuit (sortie le lendemain).
    /// <c>NULL</c> = regroupement par date calendaire (comportement historique).
    /// </summary>
    public bool? SIMPLE_NUIT { get; set; }

}
