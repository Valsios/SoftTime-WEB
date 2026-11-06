namespace SoftTime.Domain.Repositories;

public interface IDisposablePointeuseContext : IDisposable
{
    IQueryable<Entities.Pointeuse.CheckInOut> CheckInOut { get; }
    IQueryable<Entities.Pointeuse.UserInfo> Users { get; }
}
