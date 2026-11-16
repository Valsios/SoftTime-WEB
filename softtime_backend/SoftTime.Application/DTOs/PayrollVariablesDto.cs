namespace SoftTime.Application.DTOs;

/// <summary>Une des neuf variables de la configuration « Variables envoyées à la paie » d'une base.</summary>
/// <param name="Variable">Nom technique (Exo130, Exo150, I130, I150, Ferie, Dimanche, Nuit, Retard, Absence).</param>
/// <param name="Libelle">Libellé affiché (EXO 130, …).</param>
/// <param name="Actif">Envoyée à la paie.</param>
/// <param name="CodePaie">Base SAGE : code constante modifiable (Retard, Absence).</param>
/// <param name="Unite">Retard, Absence : « H » ou « J ».</param>
/// <param name="ColonneCible">Autre base : colonne cible modifiable (Retard, Absence).</param>
/// <param name="CibleExistante">Heures supplémentaires, lecture seule : code constante (SAGE) ou colonne de
/// T_PAIE_ECRITURE (autre base) déjà configurés ailleurs ; null si absent.</param>
/// <param name="SansEnvoi">Retard, Absence : l'envoi réel n'existe pas encore (lot C).</param>
public sealed record PayrollVariableDto(
    string Variable,
    string Libelle,
    bool Actif,
    string? CodePaie,
    string? Unite,
    string? ColonneCible,
    string? CibleExistante,
    bool SansEnvoi);

/// <summary>Configuration des variables de la base active.</summary>
/// <param name="Base">Nom de la base SAGE active.</param>
/// <param name="TypeBase">« STANDARD » ou « AUTRE ».</param>
/// <param name="ParDefaut">Aucune ligne enregistrée : comportement historique.</param>
/// <param name="MoisEnvoye">Une période envoyée en paie recouvre le mois civil en cours : modification impossible.</param>
/// <param name="DernierEnvoi">Période envoyée qui bloque, au format « aaaa-mm-jj → aaaa-mm-jj », sinon null.</param>
/// <param name="ColonnesCibles">Autre base : colonnes de la table d'écriture configurée (vide si inconnue).</param>
public sealed record PayrollVariablesConfigDto(
    string Base,
    string TypeBase,
    bool ParDefaut,
    bool MoisEnvoye,
    string? DernierEnvoi,
    IReadOnlyList<PayrollVariableDto> Variables,
    IReadOnlyList<string> ColonnesCibles);

/// <summary>Ligne envoyée par l'écran pour l'enregistrement.</summary>
public sealed record PayrollVariableSaveDto(string Variable, bool Actif, string? CodePaie, string? Unite, string? ColonneCible);

/// <summary>Variables actives de la base active (page Heures supplémentaires, Contrôle avant paie).</summary>
public sealed record PayrollVariablesActiveDto(string Base, IReadOnlyList<string> Actives);
