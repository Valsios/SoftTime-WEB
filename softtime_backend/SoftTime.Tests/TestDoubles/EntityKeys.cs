using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

/// <summary>Clés primaires connues des doublures en mémoire (recherche par identifiant et numérotation).</summary>
internal static class EntityKeys
{
    private static readonly HashSet<Type> KeyedTypes = new()
    {
        typeof(Responsable), typeof(Role), typeof(BddSage), typeof(BddPointeuse),
        typeof(RolePermission), typeof(AuditDroit), typeof(UserPermission), typeof(SuiviConfiguration),
    };

    /// <summary>Vrai si le type est recherché par sa clé ; sinon la doublure renvoie la première ligne.</summary>
    public static bool IsKeyed(Type type) => KeyedTypes.Contains(type);

    /// <summary>Clé primaire d'une entité à clé connue, <c>null</c> sinon.</summary>
    public static decimal? Of(object entity) => entity switch
    {
        Responsable responsable => responsable.IdResponsable,
        Role role => role.IdRole,
        BddSage sage => sage.Id,
        BddPointeuse pointeuse => pointeuse.Id,
        RolePermission permission => permission.Id,
        AuditDroit audit => audit.Id,
        UserPermission permission => permission.Id,
        SuiviConfiguration configuration => configuration.Id,
        _ => null,
    };
}
