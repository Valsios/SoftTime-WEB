namespace SoftTime.Domain.Repositories;

public interface IDisposablePointeuseContext : IDisposable
{
    IQueryable<Entities.Pointeuse.CHECKINOUT> CheckInOut { get; }
    IQueryable<Entities.Pointeuse.USERINFO> Users { get; }
}
