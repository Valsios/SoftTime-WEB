using SoftTime.Domain.Entities.Pointeuse;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Persistence;

public sealed class DisposablePointeuseContext : IDisposablePointeuseContext
{
    private readonly PointeuseDbContext _db;
    public DisposablePointeuseContext(PointeuseDbContext db) => _db = db;
    public IQueryable<CheckInOut> CheckInOut => _db.CheckInOuts;
    public IQueryable<UserInfo> Users => _db.UserInfos;
    public PointeuseDbContext Db => _db;
    public void Dispose() => _db.Dispose();
}
