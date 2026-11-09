using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.PunchProcessing;

/// <summary>
/// Pré-traitement des pointages : transforme les pointages bruts (<c>T_POINTAGE</c>) en heures
/// corrigées (<c>T_HEURECORRIGER</c>) et en anomalies à arbitrer (<c>T_HEUREANOMALIE</c>).
/// Portage de <c>Frm_Import.importa</c> de l'application desktop.
///
/// Règles :
/// <list type="bullet">
///   <item>mono-pointage : regroupement par jour calendaire, premier pointage en entrée, dernier en
///     sortie, sans appariement planning ;</item>
///   <item>multi-pointage : regroupement par journée de planning (postes de nuit compris), seuil de
///     15 minutes, dernier pointage forcé en sortie, puis parité pair → corrections / impair →
///     anomalie ;</item>
///   <item>fériés lus dans <c>T_FERIE</c> (synchronisé depuis SAGE <c>EtatJour = 1</c>).</item>
/// </list>
///
/// Invariants :
/// <list type="bullet">
///   <item>seules les lignes <c>ORIGINE = 'AUTO'</c> de la période sont purgées puis régénérées ;</item>
///   <item>les corrections manuelles (ORIGINE 'MANUEL' ou NULL) et les semaines validées
///     (<c>VALIDATION_HS</c>) ne sont jamais touchées ;</item>
///   <item>les anomalies sont additives mais une seule par (matricule, jour) : une anomalie
///     existante est mise à jour, pas dupliquée ;</item>
///   <item>les jours non travaillés sans pointage ne produisent ni heure ni anomalie.</item>
/// </list>
/// </summary>
public interface IPunchProcessingService
{
    Task<PreprocessPunchesResultDto> ProcessAsync(PeriodRequest request, CancellationToken ct = default);
}
