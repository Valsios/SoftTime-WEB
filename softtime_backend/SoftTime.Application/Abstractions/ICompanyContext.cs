namespace SoftTime.Application.Abstractions;

public interface ICompanyContext
{
    string? SageDatabase { get; }
    string? PointeuseDatabase { get; }
    void Set(string sage, string? pointeuse);
}
