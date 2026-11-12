namespace SoftTime.Application.Abstractions.Landing;

/// <summary>Charge, pour la société active, les journées de calcul HS de chaque salarié sur une période.</summary>
public interface ILandingContextLoader
{
    Task<LandingContext> LoadAsync(DateTime from, DateTime to, CancellationToken ct = default);
}
