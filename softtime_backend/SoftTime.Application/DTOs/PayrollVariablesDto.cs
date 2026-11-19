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
/// <param name="SansEnvoi">L'envoi réel de la variable n'existe pas encore (aucune depuis le lot C3).</param>
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

    /// <summary>Absence envoyée en jours : nombre d'heures d'une journée (option de la base, 8 par défaut).</summary>
    public decimal HeuresParJour { get; init; } = 8m;

    /// <summary>Codes d'absence comptés comme non justifiés dans l'Absence (lot C3).</summary>
    public PayrollAbsenceCodesDto? AbsenceCodes { get; init; }

    /// <summary>
    /// Domaines optionnels envoyés à la paie par la base (lot D5) : la cantine et les frais de déplacement activés sont exigés à
    /// la clôture. Désactivés par défaut ; seule une base STANDARD peut les activer.
    /// </summary>
    public PayrollDomainsDto Domaines { get; init; } = new(false, false);
}

/// <summary>
/// Activation des domaines optionnels de la base (T_PAIE_OPTION : DomaineCantineActif, DomaineFraisActif) et code de la
/// constante SAGE de la cantine (CodeConstanteCantine, lot D6).
/// </summary>
/// <param name="Cantine">La base envoie la cantine à la paie : la clôture l'exige.</param>
/// <param name="Frais">La base envoie les frais de déplacement à la paie : la clôture les exige.</param>
/// <param name="CodeCantine">Code de la constante SAGE de la cantine. En lecture : le code utilisé (configuré, sinon le code
/// historique CANTINE_CODE). À l'enregistrement : null = inchangé.</param>
/// <param name="CodeCantineParDefaut">En lecture : aucun code configuré (le code historique est utilisé). Ignoré à l'enregistrement.</param>
public sealed record PayrollDomainsDto(bool Cantine, bool Frais, string? CodeCantine = null, bool CodeCantineParDefaut = false);

/// <summary>
/// Réglage « Codes d'absence comptés comme non justifiés » de la base : un code par ligne de <c>T_CODEABSENCE</c>, plus les
/// codes enregistrés qui n'y figurent plus (signalés « introuvable », jamais bloquants).
/// </summary>
/// <param name="ParDefaut">Aucun réglage enregistré : « ABSENCE NON JUSTIFIEE » et l'arbitrage sans code.</param>
/// <param name="SansCode">Un arbitrage sans code est compté.</param>
public sealed record PayrollAbsenceCodesDto(bool ParDefaut, bool SansCode, IReadOnlyList<PayrollAbsenceCodeDto> Codes);

/// <summary>Un code d'absence proposé dans la carte.</summary>
/// <param name="Libelle">Libellé (celui de <c>T_CODEABSENCE</c>, ou celui enregistré si le code est introuvable).</param>
/// <param name="Compte">Compté comme non justifié.</param>
/// <param name="Introuvable">Enregistré dans le réglage mais absent de <c>T_CODEABSENCE</c>.</param>
/// <param name="NonPaye"><c>NOT_PAY</c> = 1 dans <c>T_CODEABSENCE</c>.</param>
/// <param name="Suggere">Le libellé évoque « non justifiée » ou « non payé » : simple suggestion visuelle.</param>
public sealed record PayrollAbsenceCodeDto(string Libelle, bool Compte, bool Introuvable, bool NonPaye, bool Suggere);

/// <summary>Réglage des codes d'absence comptés, tel que l'écran l'enregistre.</summary>
public sealed record PayrollAbsenceCodesSaveDto(bool SansCode, IReadOnlyList<string>? Codes);

/// <summary>Ligne envoyée par l'écran pour l'enregistrement.</summary>
public sealed record PayrollVariableSaveDto(string Variable, bool Actif, string? CodePaie, string? Unite, string? ColonneCible);

/// <summary>
/// Corps de l'enregistrement : les neuf variables et, facultatifs, l'option « remettre à 0 les salariés sans valeur à l'envoi »,
/// le nombre d'heures par jour de l'Absence et les codes d'absence comptés (champ absent ou null : inchangé). L'ancien corps,
/// un simple tableau de variables, reste accepté.
/// </summary>
public sealed record PayrollVariablesSaveRequest(IReadOnlyList<PayrollVariableSaveDto>? Variables, bool? RemiseAZeroSansValeur)
{
    /// <summary>Absence en jours : nombre d'heures d'une journée (entre 1 et 24, une décimale au plus) ; null : inchangé.</summary>
    public decimal? HeuresParJour { get; init; }

    /// <summary>Codes d'absence comptés comme non justifiés ; null : inchangés.</summary>
    public PayrollAbsenceCodesSaveDto? AbsenceCodes { get; init; }
}

/// <summary>Variables envoyées à la paie de la base active : actives ET envoyables (page Heures supplémentaires).</summary>
public sealed record PayrollVariablesActiveDto(string Base, IReadOnlyList<string> Actives)
{
    /// <summary>
    /// Option de la base : l'envoi remet à 0 les salariés du périmètre sans valeur. La page Heures supplémentaires n'annonce
    /// la remise à 0 dans sa confirmation que lorsqu'elle est active. Vrai par défaut.
    /// </summary>
    public bool RemiseAZeroSansValeur { get; init; } = true;

    /// <summary>Unité d'envoi de l'Absence (« H » ou « J ») : la confirmation de l'envoi la précise quand elle est en jours.</summary>
    public string UniteAbsence { get; init; } = "H";

    /// <summary>Absence en jours : nombre d'heures d'une journée.</summary>
    public decimal HeuresParJour { get; init; } = 8m;
}

/// <summary>Colonne d'une table de destination, telle que l'écran la propose.</summary>
/// <param name="AccepteDecimales">Peut recevoir une valeur à deux décimales (Retard, Absence).</param>
public sealed record PayrollTargetColumnDto(string Nom, string Type, bool Numerique, bool AccepteDecimales);

/// <summary>Destination d'une variable envoyable dans une autre base.</summary>
/// <param name="Colonne">Colonne de la table d'écriture, ou null si aucune.</param>
/// <param name="DecimalesRequises">Nombre de décimales que la colonne doit accepter (2 pour Retard et Absence, 0 sinon).</param>
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
