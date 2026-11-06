using Microsoft.EntityFrameworkCore;

using SoftTime.Domain.Entities.Sage;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Persistence;

public sealed class DisposableSageContext : IDisposableSageContext
{
    private readonly SageDbContext _db;
    public DisposableSageContext(SageDbContext db) => _db = db;
    public IQueryable<SalarieSage> Employees => _db.SalarieSages;
    public IQueryable<EvenementSage> Events => _db.EvenementSages;
    public IQueryable<EvenementSalarieSage> EmployeeEvents => _db.EvenementSalarieSages;
    public IQueryable<ConstanteSage> Constants => _db.ConstanteSages;
    public IQueryable<DepartementSage> Departments => _db.DepartementSages;
    public IQueryable<HistoriqueAffectationSage> Affectations => _db.HistoriqueAffectationSages;
    public IQueryable<CalendrierSocieteSage> CompanyCalendar => _db.CalendrierSocieteSages;
    public string ConnectionString => _db.Database.GetConnectionString() ?? string.Empty;
    public SageDbContext Db => _db;
    public void Dispose() => _db.Dispose();
}
