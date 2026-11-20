using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Payroll;

/// <summary>Configuration, par base SAGE, des variables envoyées à la paie (T_PAIE_VARIABLE).</summary>
public interface IPayrollVariableService
{
    /// <summary>
    /// Sélection d'une base, lue avec un cache invalidé à chaque enregistrement. Une base sans ligne reçoit
    /// <see cref="PayrollVariableSelection.Default"/> (comportement historique). Aucun contrôle de droit : appelé par
    /// les writers et le Contrôle avant paie après leur propre contrôle.
    /// </summary>
    Task<PayrollVariableSelection> GetSelectionAsync(string sageDb, CancellationToken ct = default);

    /// <summary>Variables actives de la base active (accès à la base vérifié).</summary>
    Task<PayrollVariablesActiveDto> GetActivesAsync(CancellationToken ct = default);

    /// <summary>Configuration complète de la base active, pour l'écran (droit de configuration vérifié).</summary>
    Task<PayrollVariablesConfigDto> GetConfigurationAsync(CancellationToken ct = default);

    /// <summary>
    /// Enregistre les neuf variables de la base active et, si elle est fournie, l'option « remettre à 0 les salariés sans valeur
    /// à l'envoi » (null : inchangée). Refusé si une période envoyée recouvre le mois civil en cours. Journalisé dans le journal
    /// central ; invalide les caches de la sélection et du Contrôle avant paie.
    /// </summary>
    Task<PayrollVariablesConfigDto> SaveAsync(
        IReadOnlyList<PayrollVariableSaveDto> variables, bool? remiseAZeroSansValeur = null, CancellationToken ct = default);

    /// <summary>
    /// Enregistrement complet de la carte en un seul appel et une seule sauvegarde : variables, option de remise à 0, heures
    /// par jour de l'Absence et codes d'absence comptés (champ null : inchangé). Mêmes droit, verrou de mois (409), journal
    /// et caches que <see cref="SaveAsync(IReadOnlyList{PayrollVariableSaveDto}, bool?, CancellationToken)"/>.
    /// </summary>
    Task<PayrollVariablesConfigDto> SaveAsync(PayrollVariablesSaveRequest demande, CancellationToken ct = default);

    /// <summary>
    /// Autre base : table, colonne matricule et colonne de chaque variable envoyable (T_PAIE_ECRITURE), avec les tables et
    /// colonnes lues dans la base de paie (droit de configuration vérifié).
    /// </summary>
    Task<PayrollWriteTargetsDto> GetDestinationsAsync(CancellationToken ct = default);

    /// <summary>Autre base : colonnes d'une table de la base de paie, avec leur type (droit de configuration vérifié).</summary>
    Task<IReadOnlyList<PayrollTargetColumnDto>> GetColonnesAsync(string? table, CancellationToken ct = default);

    /// <summary>
    /// Autre base : enregistre les destinations. Table et colonnes vérifiées dans la base de paie (existence, type numérique,
    /// décimales), refus si le mois en cours est déjà envoyé ; journal central et caches du Contrôle avant paie.
    /// </summary>
    Task<PayrollWriteTargetsDto> SaveDestinationsAsync(PayrollWriteTargetsSaveDto destinations, CancellationToken ct = default);

    /// <summary>
    /// Active ou désactive la cantine et les frais de déplacement pour la base active (domaines exigés à la clôture), et
    /// enregistre le code de la constante SAGE de la cantine. Même droit que la configuration (administrateur ou
    /// SAGE:{base}:UPDATE) ; auteur et date dans T_PAIE_OPTION, journal central ; caches vidés. Base STANDARD : un code saisi
    /// doit être non vide, tenir dans T_CST.CodeConstante (longueur lue dans le schéma) et exister dans T_CST avec son
    /// opérande ; l'activation de la cantine exige un code valide (un domaine activé est toujours envoyable). Base AUTRE :
    /// activation et code refusés. Pas de verrou de mois.
    /// </summary>
    Task<PayrollVariablesConfigDto> SaveDomainsAsync(PayrollDomainsDto domaines, CancellationToken ct = default);
}
