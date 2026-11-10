namespace SoftTime.Api.Middleware;

public class DroitRequirementAttribute : Attribute
{
    public int Droit { get; }
    public DroitRequirementAttribute(int droit) => Droit = droit;
}
