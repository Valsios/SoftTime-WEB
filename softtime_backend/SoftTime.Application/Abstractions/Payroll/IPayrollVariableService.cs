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
    /// Enregistre les neuf variables de la base active. Refusé si une période envoyée recouvre le mois civil en cours.
    /// Journalisé dans le journal central ; invalide les caches de la sélection et du Contrôle avant paie.
    /// </summary>
    Task<PayrollVariablesConfigDto> SaveAsync(IReadOnlyList<PayrollVariableSaveDto> variables, CancellationToken ct = default);
}
