using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// Table de sauvegarde des correspondances SAGE ↔ badge, propre au desktop.
/// <para>
/// <b>Décision : non portée, et retirée du modèle.</b> Le desktop l'utilisait comme instantané avant
/// de vider la correspondance : <c>ActiverCorrespondance</c> recopiait <c>T_CARDPAIE</c> ici, puis
/// supprimait « la totalité de la chaîne de traitement » (pointages, heures corrigées, anomalies,
/// HS, planning, catégories, tolérances) pour la paire de bases, et <c>DesactiverCorrespondance</c>
/// restaurait le tout. Le mode correspondance de l'application web est <b>non destructif</b>
/// (<c>T_ACTIVECORRESP</c> par paire de bases ; les cartes retirées sont désactivées par
/// l'auto-mapping, jamais supprimées, donc aucun identifiant n'est recréé et un instantané n'a plus
/// rien à protéger). Cette classe n'est <b>plus mappée</b> (aucun <c>DbSet</c>) : elle est conservée
/// à titre de référence pour la reprise. Voir <c>migrations/drop_legacy_cardpaie_sauve.sql</c>.
/// </para>
/// </summary>
[Table("T_CARDPAIE_SAUVE")]
public class T_CARDPAIE_SAUVE
{
    public int ID { get; set; }

    public string? SAGE_MATRICULE { get; set; }

    public string? SAGE_NOM { get; set; }

    public string? SAGE_PRENOM { get; set; }

    public string? POINTEUSE_NUMERO { get; set; }

    public string? POINTEUSE_NOM { get; set; }

    public string? SAGE_SERVEUR { get; set; }

    public string? POINTEUSE_SERVEUR { get; set; }

    public string? SAGE_BDD { get; set; }

    public string? POINTEUSE_BDD { get; set; }
}
