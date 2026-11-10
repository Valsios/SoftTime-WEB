namespace SoftTime.Application.DTOs;

/// <summary>Atterrissage de la semaine d'un salarié, en heures (voir <c>WeekLandingResult</c>).</summary>
/// <param name="SemainesDerive">Semaines pointées qui ont servi à mesurer l'écart habituel au planning.</param>
/// <param name="Niveau">0 aucun, 1 heures supplémentaires, 2 passage à 150 %, 3 plafond exonéré dépassé.</param>
public sealed record WeekLandingRowDto(
    string Matricule,
    string? Nom,
    string? Prenom,
    string? Departement,
    decimal HeuresContrat,
    decimal HeuresRealisees,
    decimal HsAcquises,
    decimal HeuresPlanifieesRestantes,
    int JoursSansPlanning,
    decimal HsSelonPlanning,
    decimal Derive,
    int SemainesDerive,
    decimal HsAtterrissage,
    int Niveau,
    decimal Seuil,
    decimal HeuresARetirer,
    bool DejaFranchi);
