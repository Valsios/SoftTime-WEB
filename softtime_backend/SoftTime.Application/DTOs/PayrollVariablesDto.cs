namespace SoftTime.Application.DTOs;

/// <summary>Une des neuf variables de la configuration « Variables envoyées à la paie » d'une base.</summary>
/// <param name="Variable">Nom technique (Exo130, Exo150, I130, I150, Ferie, Dimanche, Nuit, Retard, Absence).</param>
/// <param name="Libelle">Libellé affiché (EXO 130, …).</param>
/// <param name="Actif">Envoyée à la paie.</param>
/// <param name="CodePaie">Base SAGE : code de paie propre à la base (variables hors catalogue commun : Retard, Absence).</param>
/// <param name="Unite">Variables hors catalogue commun : « H » ou « J ».</param>
/// <param name="ColonneCible">Ancienne colonne saisie dans la configuration des variables (lot B) ; plus utilisée, la
/// colonne d'une autre base est désormais une destination de <c>T_PAIE_ECRITURE</c> (voir <c>CibleExistante</c>).</param>
/// <param name="CibleExistante">Lecture seule : code commun (base SAGE, heures supplémentaires) ou colonne de
/// <c>T_PAIE_ECRITURE</c> (autre base, toute variable envoyable) ; null si absent.</param>
/// <param name="SansEnvoi">L'envoi réel de la variable n'existe pas encore (Absence : lot C3).</param>
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
    IReadOnlyList<string> ColonnesCibles)
{
    /// <summary>Base SAGE : codes de paie lus dans SAGE (liste déroulante du code de Retard). Vide pour une autre base.</summary>
    public IReadOnlyList<SageConstantOptionDto> CodesPaie { get; init; } = Array.Empty<SageConstantOptionDto>();

    /// <summary>Base SAGE : message si la liste des codes n'a pas pu être lue (null sinon).</summary>
    public string? AvertissementCodes { get; init; }

    /// <summary>
    /// Option de la base : à l'envoi vers la paie, remettre à 0 les salariés du périmètre sans valeur (variables cochées).
    /// Vrai par défaut (aucune ligne d'option enregistrée).
    /// </summary>
    public bool RemiseAZeroSansValeur { get; init; } = true;
}

/// <summary>Ligne envoyée par l'écran pour l'enregistrement.</summary>
public sealed record PayrollVariableSaveDto(string Variable, bool Actif, string? CodePaie, string? Unite, string? ColonneCible);

/// <summary>
/// Corps de l'enregistrement : les neuf variables et, facultatif, l'option « remettre à 0 les salariés sans valeur à l'envoi »
/// (champ absent ou null : option inchangée). L'ancien corps, un simple tableau de variables, reste accepté.
/// </summary>
public sealed record PayrollVariablesSaveRequest(IReadOnlyList<PayrollVariableSaveDto>? Variables, bool? RemiseAZeroSansValeur);

/// <summary>Variables envoyées à la paie de la base active : actives ET envoyables (page Heures supplémentaires).</summary>
public sealed record PayrollVariablesActiveDto(string Base, IReadOnlyList<string> Actives)
{
    /// <summary>
    /// Option de la base : l'envoi remet à 0 les salariés du périmètre sans valeur. La page Heures supplémentaires n'annonce
    /// la remise à 0 dans sa confirmation que lorsqu'elle est active. Vrai par défaut.
    /// </summary>
    public bool RemiseAZeroSansValeur { get; init; } = true;
}

/// <summary>Colonne d'une table de destination, telle que l'écran la propose.</summary>
/// <param name="AccepteDecimales">Peut recevoir une valeur à deux décimales (Retard, en heures).</param>
public sealed record PayrollTargetColumnDto(string Nom, string Type, bool Numerique, bool AccepteDecimales);

/// <summary>Destination d'une variable envoyable dans une autre base.</summary>
/// <param name="Colonne">Colonne de la table d'écriture, ou null si aucune.</param>
/// <param name="DecimalesRequises">Nombre de décimales que la colonne doit accepter (2 pour Retard, 0 sinon).</param>
public sealed record PayrollWriteTargetDto(string Variable, string Libelle, bool Actif, string? Colonne, int DecimalesRequises);

/// <summary>Destinations d'une autre base (T_PAIE_ECRITURE) : table, colonne matricule, colonne de chaque variable.</summary>
public sealed record PayrollWriteTargetsDto(
    string Base,
    string? Table,
    string? ColonneMatricule,
    IReadOnlyList<PayrollWriteTargetDto> Variables,
    IReadOnlyList<string> Tables,
    IReadOnlyList<PayrollTargetColumnDto> Colonnes,
    bool MoisEnvoye,
    string? DernierEnvoi);

/// <summary>Destination demandée pour une variable.</summary>
public sealed record PayrollWriteTargetSaveDto(string Variable, string? Colonne);

/// <summary>Destinations envoyées par l'écran pour l'enregistrement.</summary>
public sealed record PayrollWriteTargetsSaveDto(string? Table, string? ColonneMatricule, IReadOnlyList<PayrollWriteTargetSaveDto>? Variables);
