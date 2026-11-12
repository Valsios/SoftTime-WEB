using Microsoft.EntityFrameworkCore;

using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Persistence.Configurations;
using SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

namespace SoftTime.Infrastructure.Persistence;

public class SoftTimeDbContext : DbContext
{
    private readonly AuditJournalSaveChangesInterceptor? _auditJournal;

    public SoftTimeDbContext(DbContextOptions<SoftTimeDbContext> options) : base(options) { }

    public SoftTimeDbContext(
        DbContextOptions<SoftTimeDbContext> options,
        AuditJournalSaveChangesInterceptor auditJournal) : base(options)
        => _auditJournal = auditJournal;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
        => _auditJournal?.SaveChanges(this, acceptAllChangesOnSuccess)
           ?? base.SaveChanges(acceptAllChangesOnSuccess);

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
        => _auditJournal?.SaveChangesAsync(this, acceptAllChangesOnSuccess, cancellationToken)
           ?? base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

    internal int SaveChangesWithoutAudit(bool acceptAllChangesOnSuccess)
        => base.SaveChanges(acceptAllChangesOnSuccess);

    internal Task<int> SaveChangesWithoutAuditAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken)
        => base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Responsable> Responsables => Set<Responsable>();
    public DbSet<DroitAcces> DroitAcces => Set<DroitAcces>();
    public DbSet<DroitRole> DroitRoles => Set<DroitRole>();
    public DbSet<BddSage> BddSages => Set<BddSage>();
    public DbSet<BddPointeuse> BddPointeuses => Set<BddPointeuse>();
    public DbSet<BddAutorisee> BddAutorisees => Set<BddAutorisee>();
    public DbSet<CodeAbsence> CodeAbsences => Set<CodeAbsence>();
    public DbSet<ImpressionAbsence> ImpressionAbsences => Set<ImpressionAbsence>();
    public DbSet<ImpressionRetard> ImpressionRetards => Set<ImpressionRetard>();
    public DbSet<ImpressionHeureSup> ImpressionHeureSups => Set<ImpressionHeureSup>();
    public DbSet<HeureCorrigee> HeureCorrigees => Set<HeureCorrigee>();
    public DbSet<HeureAnomalie> HeureAnomalies => Set<HeureAnomalie>();
    public DbSet<SuiviModification> SuiviModifications => Set<SuiviModification>();
    public DbSet<JournalAudit> JournalAudits => Set<JournalAudit>();
    public DbSet<SuiviTable> SuiviTables => Set<SuiviTable>();
    public DbSet<SuiviConfiguration> SuiviConfigurations => Set<SuiviConfiguration>();
    public DbSet<Frais> Frais => Set<Frais>();
    public DbSet<Cantine> Cantines => Set<Cantine>();
    public DbSet<Semaine> Semaines => Set<Semaine>();
    public DbSet<SemainePeriode> SemainePeriodes => Set<SemainePeriode>();
    public DbSet<ParametrePointeuse> ParametrePointeuses => Set<ParametrePointeuse>();
    public DbSet<CorrespondanceActive> CorrespondanceActives => Set<CorrespondanceActive>();
    public DbSet<JourFerie> JourFeries => Set<JourFerie>();
    public DbSet<HsExoImp> HsExoImps => Set<HsExoImp>();
    public DbSet<HsSemaine> HsSemaines => Set<HsSemaine>();
    public DbSet<Majoration> Majorations => Set<Majoration>();
    public DbSet<ToleranceRetard> ToleranceRetards => Set<ToleranceRetard>();
    public DbSet<HsValidee> HsValidees => Set<HsValidee>();
    public DbSet<Categorie> Categories => Set<Categorie>();
    public DbSet<CategorieSalarie> CategorieSalaries => Set<CategorieSalarie>();
    public DbSet<Pointage> Pointages => Set<Pointage>();
    public DbSet<PlanningSalarie> PlanningSalaries => Set<PlanningSalarie>();
    public DbSet<Planning> Plannings => Set<Planning>();
    public DbSet<ReposFerie> ReposFeries => Set<ReposFerie>();
    public DbSet<HeureDimanche> HeureDimanches => Set<HeureDimanche>();
    public DbSet<CardPaie> CardPaies => Set<CardPaie>();
    public DbSet<HsSalarie> HsSalaries => Set<HsSalarie>();
    public DbSet<CodeConstanteCategorie> CodeConstanteCategories => Set<CodeConstanteCategorie>();
    public DbSet<PaieEcriture> PaieEcritures => Set<PaieEcriture>();
    public DbSet<FieldRole> FieldRoles => Set<FieldRole>();
    public DbSet<SourceEntityMapping> SourceEntityMappings => Set<SourceEntityMapping>();
    public DbSet<SourceFieldMapping> SourceFieldMappings => Set<SourceFieldMapping>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AuditDroit> AuditDroits => Set<AuditDroit>();
    public DbSet<RefreshTokenRecord> RefreshTokenRecords => Set<RefreshTokenRecord>();
    public DbSet<DataMigration> DataMigrations => Set<DataMigration>();
    public DbSet<HistoriquePointage> HistoriquePointages => Set<HistoriquePointage>();
    public DbSet<HistoriqueAnomalie> HistoriqueAnomalies => Set<HistoriqueAnomalie>();
    public DbSet<HistoriqueHs> HistoriqueHs => Set<HistoriqueHs>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsOf<RoleConfiguration>();
}
