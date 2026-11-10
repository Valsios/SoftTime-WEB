using Microsoft.AspNetCore.Mvc;

namespace SoftTime.Api.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireDroitAttribute : TypeFilterAttribute
{
    public RequireDroitAttribute(int droit) : base(typeof(RequireDroitFilter))
    {
        Arguments = new object[] { droit };
    }
}
