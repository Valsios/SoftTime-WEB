namespace SoftTime.Application.DTOs;

public record LoginRequest(string Login, string Password)
{
    public LoginRequest() : this(default!, default!) { }
}

public record LoginResponse(string Token, decimal UserId, string Login, string Nom, decimal RoleId, string? Matricule, IReadOnlyCollection<int> Rights, IReadOnlyCollection<SageDbDto> AuthorizedDatabases)
{
    public LoginResponse() : this(default!, default, default!, default!, default, default, default!, default!) { }
}

public record UserDto(decimal Id, decimal RoleId, string Nom, string Login, string? Matricule, string? Password)
{
    public UserDto() : this(default, default, default!, default!, default, default) { }
}

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

public record SageDbDto(int Id, string? Serveur, string? Login, string? Password, bool? SqlAuth, string? NomBd)
{
    public SageDbDto() : this(default, default, default, default, default, default) { }
}

public record PointeuseDbDto(int Id, string? Serveur, string? Login, string? Password, bool? SqlAuth, string? NomBd, string? TypePointage, bool? Active)
{
    public PointeuseDbDto() : this(default, default, default, default, default, default, default, default) { }
}

public record ClockParamDto(int Id, bool? MultiPoint)
{
    public ClockParamDto() : this(default, default) { }
}

public record CorrespondenceModeDto(bool Active)
{
    public CorrespondenceModeDto() : this(Active:default) { }
}

public record CardPaieDto(int Id, string? SageMatricule, string? Branche, string? SageNom, string? SagePrenom, string? PointeuseNumero, string? PointeuseNom, string? SageServeur, string? PointeuseServeur, string? SageBdd, string? PointeuseBdd, DateTime? Date)
{
    public CardPaieDto() : this(default, default, default, default, default, default, default, default, default, default, default, default) { }
}

public record CategoryDto(int Id, string? Intitule, int SupervisorCardId, TimeSpan? Pause, decimal? HeuresSemaine)
{
    public CategoryDto() : this(default, default, default, default, default) { }
}

public record AffectationDto(int Id, int CardPaieId, int CategoryId)
{
    public AffectationDto() : this(default, default, default) { }
}

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

public record CodeConstanteDto(int Id, string Categorie, string? Intitule, string CodeConstante)
{
    public CodeConstanteDto() : this(default, default!, default, default!) { }
}

public record SageConstantOptionDto(string Code, string? Intitule)
{
    public SageConstantOptionDto() : this(default!, default) { }
}

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

public record AnomalyDto(decimal Id, string? Matricule, DateTime? DateIn, DateTime? DateOut, TimeSpan? H1, TimeSpan? H2, TimeSpan? HEntree, TimeSpan? HSortie, bool? AbsAm, bool? AbsPm, bool? FeriesAm, bool? FeriesPm, string? IntituleAbsence)
{
    public AnomalyDto() : this(default, default, default, default, default, default, default, default, default, default, default, default, default) { }
}

public record CorrectedHourDto(decimal Id, string? Matricule, DateTime? DateIn, DateTime? DateOut, TimeSpan? HEntree, TimeSpan? HPs, TimeSpan? HPe, TimeSpan? HSortie, bool? AbsAm, bool? AbsPm, string? IntituleAbsence, TimeSpan? Retard, bool? Ferie, bool? FerieAm, bool? FeriePm, bool? ValiderCorrection, bool? ValiderHs, decimal? Hs, decimal? MNuit, decimal? MDimanche, decimal? MFeries)
{
    public CorrectedHourDto() : this(default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default) { }
}

public record PeriodRequest(string? MatriculeFrom, string? MatriculeTo, DateTime From, DateTime To, string? Branche)
{
    public PeriodRequest() : this(default, default, default, default, default) { }
}

public record WeeklyValidationRequest(
    string? MatriculeFrom,
    string? MatriculeTo,
    DateTime From,
    DateTime To,
    string? Branche,
    IReadOnlyList<string>? SelectedMatricules) : PeriodRequest(MatriculeFrom, MatriculeTo, From, To, Branche)
{
    public WeeklyValidationRequest() : this(default, default, default, default, default, default) { }
}

public record WeeklyHsDto(int Id, string? Matricule, DateTime? DateDeb, DateTime? DateFin, decimal? Ht, decimal? Hs, decimal? Nuit, decimal? Dimanche, decimal? Feries, decimal? Retard, decimal? Absence, bool? Validated)
{
    public WeeklyHsDto() : this(default, default, default, default, default, default, default, default, default, default, default, default) { }
}

public record HsExoDto(int Oid, string? Matricule, int? NumSal, decimal? Exo130, decimal? Exo150, decimal? I130, decimal? I150, decimal? Ferie, decimal? Nuit, decimal? Dim, decimal? Retard, decimal? Absence)
{
    public HsExoDto() : this(default, default, default, default, default, default, default, default, default, default, default, default) { }
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

public record ReportFilter(string? MatriculeFrom, string? MatriculeTo, DateTime From, DateTime To, string? Branche)
{
    public ReportFilter() : this(default, default, default, default, default) { }
}