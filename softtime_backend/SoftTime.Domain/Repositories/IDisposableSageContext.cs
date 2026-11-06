namespace SoftTime.Domain.Repositories;

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
