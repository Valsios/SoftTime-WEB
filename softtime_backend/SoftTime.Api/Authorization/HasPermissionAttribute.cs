using Microsoft.AspNetCore.Authorization;

namespace SoftTime.Api.Authorization;

/// <summary>
/// Réserve une action (ou un contrôleur) aux comptes qui détiennent AU MOINS UNE des permissions fines données :
/// 403 sinon. L'administrateur les a toutes. Plusieurs attributs sur une même action se cumulent (ET).
/// Les codes viennent de <c>SoftTime.Application.Security.Permissions</c>, jamais d'une chaîne écrite en dur.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "Permission:";
    public const char Separator = '|';

    public HasPermissionAttribute(params string[] anyOf) : base(PolicyPrefix + string.Join(Separator, anyOf))
    {
        Permissions = anyOf;
    }

    public IReadOnlyList<string> Permissions { get; }
}
