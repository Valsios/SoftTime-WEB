using Microsoft.EntityFrameworkCore;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence;

public class SoftTimeDbContext : DbContext
{
    public SoftTimeDbContext(DbContextOptions<SoftTimeDbContext> options) : base(options) { }

    public DbSet<T_ROLE> T_ROLE => Set<T_ROLE>();
    public DbSet<T_RESPONSABLE> T_RESPONSABLE => Set<T_RESPONSABLE>();
    public DbSet<T_DROIT> T_DROIT => Set<T_DROIT>();
    public DbSet<T_DROIT_ROLE> T_DROIT_ROLE => Set<T_DROIT_ROLE>();
    public DbSet<T_BDD_SAGE> T_BDD_SAGE => Set<T_BDD_SAGE>();
    public DbSet<T_BDD_POINTEUSE> T_BDD_POINTEUSE => Set<T_BDD_POINTEUSE>();
    public DbSet<T_BDD_AUTORISE> T_BDD_AUTORISE => Set<T_BDD_AUTORISE>();
    public DbSet<T_CODEABSENCE> T_CODEABSENCE => Set<T_CODEABSENCE>();
    public DbSet<T_impr_ABS> T_impr_ABS => Set<T_impr_ABS>();
    public DbSet<T_impr_RET> T_impr_RET => Set<T_impr_RET>();
    public DbSet<T_impr_HS> T_impr_HS => Set<T_impr_HS>();
    public DbSet<T_HEURECORRIGER> T_HEURECORRIGER => Set<T_HEURECORRIGER>();
    public DbSet<T_HEUREANOMALIE> T_HEUREANOMALIE => Set<T_HEUREANOMALIE>();
    public DbSet<suivi_SUIVI> suivi_SUIVI => Set<suivi_SUIVI>();
    public DbSet<suivi_T> suivi_T => Set<suivi_T>();
    public DbSet<suivi_TBLCLN> suivi_TBLCLN => Set<suivi_TBLCLN>();
    public DbSet<T_FRAIS> T_FRAIS => Set<T_FRAIS>();
    public DbSet<T_CANTINE> T_CANTINE => Set<T_CANTINE>();
    public DbSet<T_Semaine> T_Semaine => Set<T_Semaine>();
    public DbSet<T_SemainePeriode> T_SemainePeriode => Set<T_SemainePeriode>();
    public DbSet<T_CLOCK> T_CLOCK => Set<T_CLOCK>();
    public DbSet<T_ACTIVECORRESP> T_ACTIVECORRESP => Set<T_ACTIVECORRESP>();
    public DbSet<T_FERIE> T_FERIE => Set<T_FERIE>();
    public DbSet<T_HSExoImp> T_HSExoImp => Set<T_HSExoImp>();
    public DbSet<T_HSSemaine> T_HSSemaine => Set<T_HSSemaine>();
    public DbSet<T_MAJORATION> T_MAJORATION => Set<T_MAJORATION>();
    public DbSet<T_TOLERANCE> T_TOLERANCE => Set<T_TOLERANCE>();
    public DbSet<T_HS_VALIDER> T_HS_VALIDER => Set<T_HS_VALIDER>();
    public DbSet<T_CARDPAIE_SAUVE> T_CARDPAIE_SAUVE => Set<T_CARDPAIE_SAUVE>();
    public DbSet<T_CATEGORIE> T_CATEGORIE => Set<T_CATEGORIE>();
    public DbSet<T_CAT_SAL> T_CAT_SAL => Set<T_CAT_SAL>();
    public DbSet<T_POINTAGE> T_POINTAGE => Set<T_POINTAGE>();
    public DbSet<T_PLANNING_SAL> T_PLANNING_SAL => Set<T_PLANNING_SAL>();
    public DbSet<T_PLANNING> T_PLANNING => Set<T_PLANNING>();
    public DbSet<T_REST_FERIE> T_REST_FERIE => Set<T_REST_FERIE>();
    public DbSet<T_HEUREDIMANCHE> T_HEUREDIMANCHE => Set<T_HEUREDIMANCHE>();
    public DbSet<T_CARDPAIE> T_CARDPAIE => Set<T_CARDPAIE>();
    public DbSet<T_HSSAL> T_HSSAL => Set<T_HSSAL>();
    public DbSet<T_CODE_CONSTANTE> T_CODE_CONSTANTE => Set<T_CODE_CONSTANTE>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<T_Semaine>().HasNoKey();
        modelBuilder.Entity<T_TOLERANCE>().ToTable("T_TOLERANCES");
        modelBuilder.Entity<T_HSSAL>().Property(e => e.Date).HasColumnName("Date");
        modelBuilder.Entity<T_PLANNING_SAL>().Property(e => e.Pause).HasColumnName("Pause");
        modelBuilder.Entity<T_MAJORATION>(e =>
        {
            e.ToTable("T_MAJORATION");
            e.Property(x => x.Cotation).HasPrecision(18, 2);
        });

        modelBuilder.Entity<T_RESPONSABLE>(e =>
        {
            e.ToTable("T_RESPONSABLE");
            e.HasKey(x => x.IDRESPONSABLE);
            e.Property(x => x.IDRESPONSABLE).HasColumnName("IDRESPONSABLE").HasPrecision(18, 0).ValueGeneratedOnAdd();
            e.Property(x => x.IDROLE).HasPrecision(18, 0);
            e.Property(x => x.LOGIN).IsRequired(false);
            e.Property(x => x.PASSWORD).IsRequired(false);
            e.Property(x => x.NOMRESPONSABLE).IsRequired(false);
        });
    }
}
