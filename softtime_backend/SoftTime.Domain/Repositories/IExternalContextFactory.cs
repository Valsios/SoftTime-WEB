namespace SoftTime.Domain.Repositories;

public interface ISageContextFactory
{
    IDisposableSageContext Create(string connectionString);
}

public interface IPointeuseContextFactory
{
    IDisposablePointeuseContext Create(string connectionString);
}

public interface IDisposableSageContext : IDisposable
{
    IQueryable<Entities.Sage.T_SAL> Employees { get; }
    IQueryable<Entities.Sage.T_GHR> Events { get; }
    IQueryable<Entities.Sage.T_GHRSAL> EmployeeEvents { get; }
    IQueryable<Entities.Sage.T_CST> Constants { get; }
    IQueryable<Entities.Sage.T_DEPARTEMENT> Departments { get; }
    IQueryable<Entities.Sage.T_HST_AFFECTATION> Affectations { get; }
    IQueryable<Entities.Sage.T_GHRCAL_SOCIETE> CompanyCalendar { get; }
    string ConnectionString { get; }
}

public interface IDisposablePointeuseContext : IDisposable
{
    IQueryable<Entities.Pointeuse.CHECKINOUT> CheckInOut { get; }
    IQueryable<Entities.Pointeuse.USERINFO> Users { get; }
}
