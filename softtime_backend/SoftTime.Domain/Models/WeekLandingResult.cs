namespace SoftTime.Domain.Models;

/// <summary>Atterrissage de la semaine d'un salarié (voir <c>WeekLandingRules</c>), en heures.</summary>
/// <param name="HeuresRealisees">Heures créditées sur les jours déjà pointés.</param>
/// <param name="HsAcquises">Heures supplémentaires déjà acquises sur les jours pointés.</param>
/// <param name="HeuresPlanifieesRestantes">Heures que le planning prévoit encore d'ici la fin de semaine.</param>
/// <param name="JoursSansPlanning">Jours ouvrés restants sans planning saisi (atterrissage sous-estimé).</param>
/// <param name="HsSelonPlanning">Heures supplémentaires si le planning restant est suivi à la lettre.</param>
/// <param name="Derive">Part de l'écart habituel au planning appliquée aux heures restantes.</param>
/// <param name="HsAtterrissage">Heures supplémentaires attendues en fin de semaine, écart habituel compris.</param>
/// <param name="Niveau">Seuil le plus élevé franchi par l'atterrissage.</param>
/// <param name="Seuil">Valeur du seuil franchi, en heures supplémentaires.</param>
/// <param name="HeuresARetirer">Heures à retirer du planning restant pour repasser sous le seuil.</param>
/// <param name="DejaFranchi">Le seuil est franchi par les heures déjà faites : il n'est plus évitable.</param>
public sealed record WeekLandingResult(
    decimal HeuresRealisees,
    decimal HsAcquises,
    decimal HeuresPlanifieesRestantes,
    int JoursSansPlanning,
    decimal HsSelonPlanning,
    decimal Derive,
    decimal HsAtterrissage,
    LandingLevel Niveau,
    decimal Seuil,
    decimal HeuresARetirer,
    bool DejaFranchi);
