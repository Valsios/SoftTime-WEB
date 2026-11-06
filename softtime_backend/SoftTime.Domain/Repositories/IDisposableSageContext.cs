namespace SoftTime.Domain.Repositories;

public interface IDisposableSageContext : IDisposable
{
    IQueryable<Entities.Sage.SalarieSage> Employees { get; }
    IQueryable<Entities.Sage.EvenementSage> Events { get; }
    IQueryable<Entities.Sage.EvenementSalarieSage> EmployeeEvents { get; }
    IQueryable<Entities.Sage.ConstanteSage> Constants { get; }
    IQueryable<Entities.Sage.DepartementSage> Departments { get; }
    IQueryable<Entities.Sage.HistoriqueAffectationSage> Affectations { get; }
    IQueryable<Entities.Sage.CalendrierSocieteSage> CompanyCalendar { get; }
    string ConnectionString { get; }
}
