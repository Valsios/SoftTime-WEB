using Microsoft.EntityFrameworkCore;

using SoftTime.Domain.Entities.Sage;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Persistence;

public sealed class DisposableSageContext : IDisposableSageContext
{
    private readonly SageDbContext _db;
    public DisposableSageContext(SageDbContext db) => _db = db;
    public IQueryable<T_SAL> Employees => _db.T_SAL;
    public IQueryable<T_GHR> Events => _db.T_GHR;
    public IQueryable<T_GHRSAL> EmployeeEvents => _db.T_GHRSAL;
    public IQueryable<T_CST> Constants => _db.T_CST;
    public IQueryable<T_DEPARTEMENT> Departments => _db.T_DEPARTEMENT;
    public IQueryable<T_HST_AFFECTATION> Affectations => _db.T_HST_AFFECTATION;
    public IQueryable<T_GHRCAL_SOCIETE> CompanyCalendar => _db.T_GHRCAL_SOCIETE;
    public string ConnectionString => _db.Database.GetConnectionString() ?? string.Empty;
    public SageDbContext Db => _db;
    public void Dispose() => _db.Dispose();
}
