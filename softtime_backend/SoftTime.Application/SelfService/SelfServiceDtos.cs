namespace SoftTime.Application.SelfService;

// Réponses de « Mon espace » : aucune ligne n'expose d'identifiant de table, de matricule ni de nom, et jamais les données d'un
// autre salarié. Les dates sont des jours (AAAA-MM-JJ) ; les durées sont en heures ou en minutes comme indiqué.

/// <summary>Période demandée (jours inclusifs), après bornage.</summary>
public sealed record SelfPeriodDto(DateTime From, DateTime To);

/// <summary>Une semaine validée (lundi → dimanche) : heures et répartition 130 % / 150 % (règle OvertimeRules.SplitWeekly).</summary>
public sealed record SelfWeekDto(
    DateTime Lundi,
    DateTime Dimanche,
    decimal HeuresTravaillees,
    decimal HeuresSupplementaires,
    decimal Hs130,
    decimal Hs150,
    decimal Nuit,
    decimal Dimanches,
    decimal Feries,
    decimal Retard,
    decimal Absence);

/// <summary>Cumul EXO / IMPO d'une période de calcul (exonéré et imposable, à 130 % et à 150 %).</summary>
public sealed record SelfExoImpoDto(
    DateTime PeriodeDebut,
    DateTime PeriodeFin,
    decimal Exo130,
    decimal Exo150,
    decimal Imp130,
    decimal Imp150);

public sealed record SelfWeeksDto(
    SelfPeriodDto Periode,
    IReadOnlyList<SelfWeekDto> Semaines,
    IReadOnlyList<SelfExoImpoDto> ExoImpo);

/// <summary>Une anomalie du jour : <c>Nature</c> est un libellé (« Absence sans pointage », « Pointage à vérifier »).</summary>
public sealed record SelfAnomalyDto(DateTime Date, string Nature, TimeSpan? Heure1, TimeSpan? Heure2, bool Ferie);

public sealed record SelfAnomaliesDto(SelfPeriodDto Periode, IReadOnlyList<SelfAnomalyDto> Anomalies);

/// <summary>Absence ou congé constaté (Congé SAGE, Arbitrée GTA, Non justifiée…) avec sa durée planifiée en heures.</summary>
public sealed record SelfAbsenceDto(DateTime Date, string Periode, string Type, decimal? DureeHeures, string? Motif);

public sealed record SelfAbsencesDto(SelfPeriodDto Periode, IReadOnlyList<SelfAbsenceDto> Absences);

public sealed record SelfDelayDto(DateTime Date, TimeSpan? HeureEntree, TimeSpan? HeurePrevue, int ToleranceMinutes, int MinutesRetard, bool RetardSaisi);

public sealed record SelfDelaysDto(SelfPeriodDto Periode, IReadOnlyList<SelfDelayDto> Retards);

/// <summary>Synthèse de la période : comptages seulement.</summary>
public sealed record SelfSummaryDto(
    SelfPeriodDto Periode,
    int SemainesValidees,
    decimal HeuresSupplementaires,
    decimal Hs130,
    decimal Hs150,
    int Anomalies,
    int JoursAbsenceNonJustifiee,
    int JoursConge,
    int Retards,
    int MinutesRetardTotal);
