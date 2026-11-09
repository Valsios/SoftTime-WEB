namespace SoftTime.Application.Abstractions.RequestContext;

public interface ICompanyContext
{
    string? SageDatabase { get; }
    string? PointeuseDatabase { get; }
    void Set(string sage, string? pointeuse);
}
