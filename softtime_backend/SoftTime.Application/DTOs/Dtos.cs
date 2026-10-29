global using SoftTime.Domain.Models;

namespace SoftTime.Application.DTOs;

public record LoginRequest(string Login, string Password)
{
    public LoginRequest() : this(default!, default!) { }
}

public record LoginResponse(string Token, decimal UserId, string Login, string Nom, decimal RoleId, string? Matricule, IReadOnlyCollection<int> Rights, IReadOnlyCollection<SageDbDto> AuthorizedDatabases, IReadOnlyCollection<string>? Permissions = null, bool PermissionsCustomized = false)
{
    public LoginResponse() : this(default!, default, default!, default!, default, default, default!, default!, null, false) { }
}

public record UserDto(decimal Id, decimal RoleId, string Nom, string Login, string? Matricule, string? Password)
{
    /// <summary>Adresse d'envoi des identifiants (facultative).</summary>
    public string? Email { get; init; }

    public UserDto() : this(default, default, default!, default!, default, default) { }
}

/// <summary>Création du premier compte administrateur (installation vierge).</summary>
public record AdminSetupDto(string Nom, string Login, string Password, string? Email)
{
    public AdminSetupDto() : this(string.Empty, string.Empty, string.Empty, null) { }
}

/// <summary>Résultat de la création du compte initial : indique si les identifiants ont pu être envoyés.</summary>
public record AdminSetupResultDto(decimal UserId, string Login, bool EmailEnvoye, string Message);

/// <summary>Une règle de la politique de mot de passe, avec son libellé prêt à afficher.</summary>
public record PasswordPolicyRuleDto(string Code, string Label);

/// <summary>
/// Politique de mot de passe exposée au client : longueur minimale et règles affichables,
/// pour que l'interface valide exactement comme le serveur.
/// </summary>
public record PasswordPolicyDto(int MinLength, IReadOnlyList<PasswordPolicyRuleDto> Rules);

public record RoleDto(decimal Id, string Nom)
{
    public RoleDto() : this(default, default!) { }
}

public record DroitDto(decimal Id, string Nom)
{
    public DroitDto() : this(default, default!) { }
}

public record PrivilegeDto(decimal Id, decimal RoleId, decimal DroitId)
{
    public PrivilegeDto() : this(default, default, default) { }
}

public record DbAccessDto(int Id, decimal UserId, int SageDbId)
{
    public DbAccessDto() : this(default, default, default) { }
}

public record SageDbDto(
    int Id,
    string? Serveur,
    string? Login,
    string? Password,
    bool? SqlAuth,
    string? NomBd,
    string TypeBase = "STANDARD",
    IReadOnlyList<SourceEntityMappingDto>? Mappings = null)
{
    public SageDbDto() : this(default, default, default, default, default, default) { }
}
public record PointeuseDbDto(
    int Id,
    string? Serveur,
    string? Login,
    string? Password,
    bool? SqlAuth,
    string? NomBd,
    string? TypePointage,
    bool? Active,
    string TypeBase = "STANDARD",
    IReadOnlyList<SourceEntityMappingDto>? Mappings = null)
{
    public PointeuseDbDto() : this(default, default, default, default, default, default, default, default) { }
}

public record TestSageConnectionDto(string? Serveur, bool? SqlAuth, string? Login, string? Password, string? NomBd, string TypeBase, IReadOnlyList<SourceEntityMappingDto>? Mappings = null);
public record TestPointeuseConnectionDto(string? Serveur, bool? SqlAuth, string? Login, string? Password, string? NomBd, string TypeBase, IReadOnlyList<SourceEntityMappingDto>? Mappings = null);
public record ConnectionTestResultDto(bool Success, string Message, IReadOnlyList<string>? MissingTables = null);

public record FieldRoleDto(string Code, string? Label, string SystemType, string EntityKind, bool IsRequired, string? AutoMappingPatterns, int SortOrder);

public record SourceFieldMappingDto(int Id, string FieldRoleCode, string? SourceColumn)
{
    public SourceFieldMappingDto() : this(default, default!, default) { }
}

public record SourceEntityMappingDto(int Id, string? SystemType, int? SageDbId, int? PointeuseDbId, string EntityKind, string? SourceTable, IReadOnlyList<SourceFieldMappingDto> Fields)
{
    public SourceEntityMappingDto() : this(default, default, default, default, default!, default, Array.Empty<SourceFieldMappingDto>()) { }
}

public record ClockParamDto(int Id, bool? MultiPoint)
{
    public ClockParamDto() : this(default, default) { }
}

public record CorrespondenceModeDto(bool Active)
{
    public CorrespondenceModeDto() : this(Active:default) { }
}

public record CardPaieDto(
    int Id, string? SageMatricule, string? Branche, string? SageNom, string? SagePrenom,
    string? PointeuseNumero, string? PointeuseNom, string? SageServeur, string? PointeuseServeur,
    string? SageBdd, string? PointeuseBdd, DateTime? Date,
    string Origine = "AUTO", bool Actif = true, string? Departement = null)
{
    public CardPaieDto() : this(default, default, default, default, default, default, default, default, default, default, default, default) { }
}
public record SyncResultDto(int Added, int Deactivated, string Message);

public record ActivationResultDto(bool HolidaysImported, int CardsAdded, int CardsDeactivated, string Message);

public record CategoryDto(int Id, string? Intitule, int SupervisorCardId, TimeSpan? Pause, decimal? HeuresSemaine)
{
    public CategoryDto() : this(default, default, default, default, default) { }
}

public record AffectationDto(int Id, int CardPaieId, int CategoryId)
{
    public AffectationDto() : this(default, default, default) { }
}

public record AffectationRowDto(
    int Id,
    int CardPaieId,
    int CategoryId,
    string? Matricule,
    string? Nom,
    string? Prenom,
    string? Departement,
    string? Service);

public record AffectationOptionsDto(
    IReadOnlyList<string> Departements,
    IReadOnlyList<string> Services);

public record ToleranceDto(int Id, int CategoryId, int Tolerance, bool TypesTol, int? Sortie)
{
    public ToleranceDto() : this(default, default, default, default, default) { }
}

public record HolidayDto(int Id, string? Intitule, DateTime? Date)
{
    public HolidayDto() : this(default, default, default) { }
}

public record MajorationDto(int Id, string? Mojoration, decimal? Cotation)
{
    public MajorationDto() : this(default, default, default) { }
}

public record DepartementServiceDto(string Matricule, string? Departement, string? Service);
public record DiscoverDatabasesDto(string Serveur, bool SqlAuth, string? Login, string? Password);
public record DiscoverTablesDto(string Serveur, string Base, bool SqlAuth, string? Login, string? Password);
public record DiscoverColumnsDto(string Serveur, string Base, string Table, bool SqlAuth, string? Login, string? Password);

public record CodeConstanteDto(int Id, string Categorie, string? Intitule, string CodeConstante, string? TableCible = null)
{
    public CodeConstanteDto() : this(default, default!, default, default!) { }
}

public record SageConstantOptionDto(string Code, string? Intitule)
{
    public SageConstantOptionDto() : this(default!, default) { }
}

public record DepartementServiceOptionsDto(IReadOnlyList<string> Departements, IReadOnlyList<string> Services);

public record OvertimeSageCodes(string Exo130, string Exo150, string I130, string I150, string Ferie, string Dim, string Nuit)
{
    public OvertimeSageCodes() : this("HS01", "HS02", "HS03", "HS04", "HS05", "HS06", "HS07") { }
}

public record AbsenceCodeDto(int Id, string? Intitule, bool? NotPay)
{
    public AbsenceCodeDto() : this(default, default, default) { }
}

public record ShiftDto(decimal Id, int? NoShift, TimeSpan? Ha, TimeSpan? Pause, TimeSpan? Hd, string? Intitule)
{
    public ShiftDto() : this(default, default, default, default, default, default) { }
}

public record EmployeePlanningDto(int Id, int CardPaieId, int? NoShift, DateTime? DateP, DateTime? Ha, TimeSpan? Pause, DateTime? Hd, bool? Off, string? Matricule = null, string? Nom = null, string? Prenom = null)
{
    public EmployeePlanningDto() : this(default, default, default, default, default, default, default, default) { }
}

public record PlanningEmployeeDto(int CardPaieId, string? Matricule, string? Nom, string? Prenom)
{
    public PlanningEmployeeDto() : this(default, default, default, default) { }
}

/// <summary>Salarié + nombre de jours planifiés sur la période, pour la grille paginée du planning.</summary>
public record PlanningEmployeeStatDto(int CardPaieId, string? Matricule, string? Nom, string? Prenom, int PlannedDays)
{
    public PlanningEmployeeStatDto() : this(default, default, default, default, default) { }
}

public record PunchDto(decimal Id, string? Matricule, DateTime? Date, TimeSpan? Heure, string? Type, DateTime? ImportDate)
{
    public PunchDto() : this(default, default, default, default, default, default) { }
}

public record ImportPunchesRequest(string? MatriculeFrom, string? MatriculeTo, DateTime From, DateTime To)
{
    public ImportPunchesRequest() : this(default, default, default, default) { }
}

public record ImportResultDto(int Imported, int Skipped, string Message)
{
    public ImportResultDto() : this(default, default, default!) { }
}

/// <summary>Résultat du pré-traitement des pointages (pointage → heures corrigées + anomalies).</summary>
public record PreprocessPunchesResultDto(int CorrectionsCreees, int AnomaliesCreees, int JoursIgnores, string Message)
{
    public PreprocessPunchesResultDto() : this(default, default, default, default!) { }
}

public record AnomalyDto(decimal Id, string? Matricule, DateTime? DateIn, DateTime? DateOut, TimeSpan? H1, TimeSpan? H2, TimeSpan? HEntree, TimeSpan? HPs, TimeSpan? HPe, TimeSpan? HSortie, bool? AbsAm, bool? AbsPm, bool? Feries, bool? FeriesAm, bool? FeriesPm, string? IntituleAbsence)
{
    public AnomalyDto() : this(default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default) { }
}

/// <summary>
/// Arbitrage d'une anomalie : heures retenues et drapeaux d'absence / férié.
/// Les propriétés non renseignées (null) laissent la valeur existante inchangée.
/// </summary>
public record AnomalyUpdateDto(DateTime? DateIn, DateTime? DateOut, TimeSpan? HEntree, TimeSpan? HPs, TimeSpan? HPe, TimeSpan? HSortie, bool? AbsAm, bool? AbsPm, bool? Feries, bool? FeriesAm, bool? FeriesPm, string? IntituleAbsence)
{
    public AnomalyUpdateDto() : this(default, default, default, default, default, default, default, default, default, default, default, default) { }
}

/// <summary>Validation en masse : les anomalies sélectionnées deviennent des heures corrigées.</summary>
public record AnomalyValidationRequest(List<decimal> Ids)
{
    public AnomalyValidationRequest() : this(new List<decimal>()) { }
}

public record AnomalyValidationResultDto(int CorrectionsCreees, int AnomaliesSupprimees, string Message)
{
    public AnomalyValidationResultDto() : this(default, default, default!) { }
}

// --- Archives des suppressions (ex-tables « Historique* » du desktop) ---

public record HistoryPointageDto(decimal Id, string? Matricule, DateTime? Date, TimeSpan? Heure, string? Type, DateTime SupprimeLe)
{
    public HistoryPointageDto() : this(default, default, default, default, default, default) { }
}

public record HistoryAnomalieDto(decimal Id, string? Matricule, DateTime? DateIn, DateTime? DateOut, TimeSpan? HEntree, TimeSpan? HSortie, bool? AbsAm, bool? AbsPm, string? IntituleAbsence, DateTime SupprimeLe)
{
    public HistoryAnomalieDto() : this(default, default, default, default, default, default, default, default, default, default) { }
}

public record HistoryHsDto(decimal Id, string? Matricule, DateTime? PeriodeDebut, DateTime? PeriodeFin, decimal? Exo130, decimal? Exo150, decimal? I130, decimal? I150, DateTime SupprimeLe)
{
    public HistoryHsDto() : this(default, default, default, default, default, default, default, default, default) { }
}

public record HistoryDeleteResultDto(int Supprimees, string Message)
{
    public HistoryDeleteResultDto() : this(default, default!) { }
}

// --- Corrections et purges de la période de traitement ---

/// <summary>Correction d'un pointage importé : les champs null restent inchangés.</summary>
public record PunchUpdateDto(DateTime? Date, TimeSpan? Heure, string? Type)
{
    public PunchUpdateDto() : this(default, default, default) { }
}

/// <summary>Résultat de la purge d'une période de traitement.</summary>
public record PurgePunchesResultDto(int Pointages, int Corrections, int Anomalies, int CorrectionsManuellesConservees, string Message)
{
    public PurgePunchesResultDto() : this(default, default, default, default, default!) { }
}

/// <summary>Résultat de la purge des validations d'une semaine.</summary>
public record PurgeValidationResultDto(int Validations, int Corrections, string Message)
{
    public PurgeValidationResultDto() : this(default, default, default!) { }
}

// --- Tableau de bord analytique ---

public record DashboardStatsDto(
    int TotalEmployes,
    DashboardKpiDto Kpi,
    DashboardAbsencesDto Absences,
    DashboardRetardsDto Retards,
    IReadOnlyList<DashboardSerieDto> Series,
    DashboardRepartitionDto Repartition,
    DashboardAssiduiteDto Assiduite)
{
    public DashboardStatsDto() : this(default, default!, default!, default!,
        Array.Empty<DashboardSerieDto>(), default!, default!) { }
}

/// <param name="JoursPlanifies">
/// Présence théorique de la période : jours de planning non off, hors week-end et férié.
/// </param>
/// <param name="TauxAbsenteisme">Jours d'absence / jours planifiés, en pourcentage.</param>
/// <param name="TauxPonctualite">
/// Jours travaillés sans retard / jours travaillés, en pourcentage.
/// </param>
public record DashboardKpiDto(
    int CartesActives,
    int Anomalies,
    int CorrectionsManuelles,
    int SemainesValidees,
    int SemainesEnAttente,
    decimal HeuresTravaillees,
    decimal HeuresSupplementaires,
    int JoursPlanifies,
    int JoursTravailles,
    int JoursAbsence,
    int JoursRetard,
    int MinutesRetard,
    decimal TauxAbsenteisme,
    decimal TauxPonctualite)
{
    public DashboardKpiDto() : this(default, default, default, default, default, default, default,
        default, default, default, default, default, default, default) { }
}

/// <summary>Point d'une courbe d'évolution (une période = un regroupement).</summary>
public record DashboardSerieDto(string Periode, decimal Heures, decimal HeuresSupplementaires)
{
    public DashboardSerieDto() : this(string.Empty, default, default) { }
}

/// <param name="Total">Absences explicites + jours planifiés sans pointage.</param>
/// <param name="PlanifiesSansPointage">
/// Jours planifiés (OffP = false) sans heure corrigée, hors week-end et férié.
/// Les congés SAGE ne sont pas encore exclus de ce compteur (voir LOT 9).
/// </param>
public record DashboardAbsencesDto(int Total, int Explicites, int PlanifiesSansPointage, IReadOnlyList<DashboardGroupDto> Stats, IReadOnlyList<DashboardTopDto> Top)
{
    public DashboardAbsencesDto() : this(default, default, default, Array.Empty<DashboardGroupDto>(), Array.Empty<DashboardTopDto>()) { }
}

public record DashboardRetardsDto(int Total, int TotalMinutes, IReadOnlyList<DashboardGroupDto> Stats, IReadOnlyList<DashboardTopDto> Top)
{
    public DashboardRetardsDto() : this(default, default, Array.Empty<DashboardGroupDto>(), Array.Empty<DashboardTopDto>()) { }
}

public record DashboardGroupDto(string Periode, int Count, int TotalMinutes)
{
    public DashboardGroupDto() : this(string.Empty, default, default) { }
}

public record DashboardTopDto(string Matricule, string? Nom, string? Prenom, string? Departement, int Count, int TotalMinutes)
{
    public DashboardTopDto() : this(string.Empty, default, default, default, default, default) { }
}

/// <summary>Répartition de l'effectif et de l'activité d'un axe (catégorie, branche, département).</summary>
public record DashboardSliceDto(string Libelle, int Effectif, int Absences, int Retards, int MinutesRetard, decimal HeuresSupplementaires)
{
    public DashboardSliceDto() : this(string.Empty, default, default, default, default, default) { }
}

public record DashboardRepartitionDto(
    IReadOnlyList<DashboardSliceDto> ParCategorie,
    IReadOnlyList<DashboardSliceDto> ParBranche,
    IReadOnlyList<DashboardSliceDto> ParDepartement)
{
    public DashboardRepartitionDto() : this(Array.Empty<DashboardSliceDto>(), Array.Empty<DashboardSliceDto>(), Array.Empty<DashboardSliceDto>()) { }
}

/// <param name="TauxPonctualite">Jours travaillés sans retard / jours travaillés, en pourcentage.</param>
public record DashboardAssiduiteRowDto(
    string Matricule, string? Nom, string? Prenom, string? Departement, string? Categorie,
    int JoursTravailles, int JoursAbsence, int JoursRetard, int MinutesRetard, decimal TauxPonctualite)
{
    public DashboardAssiduiteRowDto() : this(string.Empty, default, default, default, default,
        default, default, default, default, default) { }
}

/// <summary>Classements d'assiduité : les plus ponctuels et les moins ponctuels de la période.</summary>
public record DashboardAssiduiteDto(
    IReadOnlyList<DashboardAssiduiteRowDto> PlusPonctuels,
    IReadOnlyList<DashboardAssiduiteRowDto> MoinsPonctuels)
{
    public DashboardAssiduiteDto() : this(Array.Empty<DashboardAssiduiteRowDto>(), Array.Empty<DashboardAssiduiteRowDto>()) { }
}

/// <summary>Valeurs disponibles pour les filtres du tableau de bord analytique.</summary>
public record DashboardFiltersDto(
    IReadOnlyList<string> Departements,
    IReadOnlyList<string> Branches,
    IReadOnlyList<DashboardOptionDto> Categories)
{
    public DashboardFiltersDto() : this(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<DashboardOptionDto>()) { }
}

public record DashboardOptionDto(int Id, string Libelle)
{
    public DashboardOptionDto() : this(default, string.Empty) { }
}

public record CorrectedHourDto(decimal Id, string? Matricule, DateTime? DateIn, DateTime? DateOut, TimeSpan? HEntree, TimeSpan? HPs, TimeSpan? HPe, TimeSpan? HSortie, bool? AbsAm, bool? AbsPm, string? IntituleAbsence, TimeSpan? Retard, bool? Ferie, bool? FerieAm, bool? FeriePm, bool? ValiderCorrection, bool? ValiderHs, decimal? Hs, decimal? MNuit, decimal? MDimanche, decimal? MFeries)
{
    public CorrectedHourDto() : this(default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default) { }
}

public record PeriodRequest(string? MatriculeFrom, string? MatriculeTo, DateTime From, DateTime To, string? Branche, string? Departement = null, string? Search = null)
{
    public PeriodRequest() : this(default, default, default, default, default, default, default) { }
}

public record WeeklyValidationRequest(
    string? MatriculeFrom,
    string? MatriculeTo,
    DateTime From,
    DateTime To,
    string? Branche,
    IReadOnlyList<string>? SelectedMatricules,
    string? Departement = null) : PeriodRequest(MatriculeFrom, MatriculeTo, From, To, Branche, Departement)
{
    public WeeklyValidationRequest() : this(default, default, default, default, default, default, default) { }
}

public record WeeklyHsDto(int Id, string? Matricule, DateTime? DateDeb, DateTime? DateFin, decimal? Ht, decimal? Hs, decimal? Nuit, decimal? Dimanche, decimal? Feries, decimal? Retard, decimal? Absence, bool? Validated)
{
    public WeeklyHsDto() : this(default, default, default, default, default, default, default, default, default, default, default, default) { }
}

public record HsExoDto(int Oid, string? Matricule, int? NumSal, decimal? Exo130, decimal? Exo150, decimal? I130, decimal? I150, decimal? Ferie, decimal? Nuit, decimal? Dim, decimal? Retard, decimal? Absence)
{
    public HsExoDto() : this(default, default, default, default, default, default, default, default, default, default, default, default) { }
}

/// <summary>Jour détaillé du calcul des heures supplémentaires d'un salarié.</summary>
public record OvertimeDetailDayDto(
    DateTime Date,
    TimeSpan? Entree,
    TimeSpan? Sortie,
    decimal HeuresTravaillees,
    decimal HeuresSupJournalieres,
    decimal HeuresNuit,
    bool Dimanche,
    bool Ferie,
    TimeSpan? Retard,
    bool AbsenceAm,
    bool AbsencePm,
    bool Conge,
    bool JourTravaille,
    decimal HeuresReelles)
{
    public OvertimeDetailDayDto() : this(default, default, default, default, default, default, default, default, default, default, default, default, default, default) { }
}

/// <summary>
/// Semaine du calcul EXO/IMPO d'un salarié (répartition 130 % / 150 %). <see cref="Debut"/> et
/// <see cref="Fin"/> sont les bornes lundi → dimanche de la semaine ; la période du calcul, elle,
/// est affichée dans l'en-tête du détail et stockée dans <c>T_HSSemaine.PeriodeDebut/PeriodeFin</c>.
/// </summary>
public record OvertimeDetailWeekDto(int? Semaine, DateTime? Debut, DateTime? Fin, decimal Hs, decimal Hs1, decimal Hs2)
{
    public OvertimeDetailWeekDto() : this(default, default, default, default, default, default) { }
}

/// <summary>Détail du calcul des heures supplémentaires d'un salarié (remplace le <c>DetailHS</c> du desktop).</summary>
public record OvertimeDetailDto(
    string? Matricule,
    DateTime From,
    DateTime To,
    IReadOnlyList<OvertimeDetailDayDto> Jours,
    IReadOnlyList<OvertimeDetailWeekDto> Semaines,
    HsExoDto? ExoImpo)
{
    public OvertimeDetailDto() : this(default, default, default, new List<OvertimeDetailDayDto>(), new List<OvertimeDetailWeekDto>(), default) { }
}

public record PunchCountDto(string? Matricule, int Total);

public record CanteenDto(int Id, string? Matricule, DateTime? DateDeb, DateTime? DateFin, int? Total)
{
    public CanteenDto() : this(default, default, default, default, default) { }
}

public record TravelDto(int Id, string? Matricule, DateTime? DateDeb, DateTime? DateFin, int? Total)
{
    public TravelDto() : this(default, default, default, default, default) { }
}

/// <summary>Anomalie du calcul cantine : le total du jour est contradictoire (pointage vs absence déclarée).</summary>
public record CanteenAnomalyDto(string? Matricule, DateTime Date, string Motif)
{
    public CanteenAnomalyDto() : this(default, default, string.Empty) { }
}

/// <summary>Résultat de la synchronisation des cumuls cantine / frais vers SAGE.</summary>
public record ExtrasSyncResultDto(string Database, string TypeBase, int EmployeesSynced, int CumulsUpdated, string Message);

/// <summary>Résultat de la purge des lignes cantine / frais d'une période.</summary>
public record PurgeExtrasResultDto(int LignesSupprimees, string Message);

public record AuditEventDto(int Id, string? Table, string? Column, string? User, string? Action, DateTime? Date, string? NewValue, string? OldValue, string? Base)
{
    public AuditEventDto() : this(default, default, default, default, default, default, default, default, default) { }
}

public record AuditConfigDto(int Id, string? Table, string? Column, string? Type, bool? Enabled, string? Base)
{
    public AuditConfigDto() : this(default, default, default, default, default, default) { }
}

public record ExcelExportRequest(string FileName, string SheetName, List<string> Headers, List<List<string?>> Rows)
{
    public ExcelExportRequest() : this("export.xlsx", "Données", new List<string>(), new List<List<string?>>()) { }
}

public record ReportFilter(string? MatriculeFrom, string? MatriculeTo, DateTime From, DateTime To, string? Branche, string? Search = null)
{
    public ReportFilter() : this(default, default, default, default, default) { }
}

public record PayrollWriteConfigurationDto(int Id, string? TableCible, string? ColMatricule, string Categorie, string? ColValeur)
{
    public PayrollWriteConfigurationDto() : this(default, default, default, "Exo130", default) { }
}

public record PayrollWriteResultDto(string Database, string TypeBase, int EmployeesUpdated, int CategoriesUpdated, string Message);

public record UserDetailedPermissionsDto(
    decimal UserId,
    IReadOnlyList<string> GrantedPermissions,
    bool IsCustomized
)
{
    public UserDetailedPermissionsDto() : this(0, Array.Empty<string>(), false) { }
}

public record SaveUserDetailedPermissionsRequest(
    decimal UserId,
    IReadOnlyList<string> GrantedPermissions
)
{
    public SaveUserDetailedPermissionsRequest() : this(0, Array.Empty<string>()) { }
}
