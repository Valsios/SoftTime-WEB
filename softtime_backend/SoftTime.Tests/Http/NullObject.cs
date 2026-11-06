using System.Reflection;

namespace SoftTime.Tests.Http;

/// <summary>Double « objet nul » : toute méthode renvoie une valeur vide, aucune connexion externe.</summary>
public class NullObject : DispatchProxy
{
    public static T Create<T>() where T : class => Create<T, NullObject>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var rt = targetMethod!.ReturnType;
        if (rt == typeof(void)) return null;
        if (rt == typeof(Task)) return Task.CompletedTask;
        if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var inner = rt.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, new[] { Empty(inner) });
        }
        return Empty(rt);
    }

    private static object? Empty(Type t)
    {
        if (t.IsValueType) return Activator.CreateInstance(t);
        if (t == typeof(string)) return string.Empty;
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(IReadOnlyList<>) || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyCollection<>)
                || def == typeof(IList<>) || def == typeof(ICollection<>))
                return Array.CreateInstance(t.GetGenericArguments()[0], 0);
        }
        return null;
    }
}
