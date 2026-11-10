namespace SoftTime.Application.Security;

/// <summary>Résultat de <see cref="EffectiveRightsCalculator.ConvertLegacyList"/> (simulation, rien n'est appliqué).</summary>
public sealed record LegacyConversion(
    IReadOnlyList<string> Additions,
    IReadOnlyList<string> Removals,
    IReadOnlyList<string> IgnoredUnknown)
{
    public void Deconstruct(out IReadOnlyList<string> additions, out IReadOnlyList<string> removals)
    {
        additions = Additions;
        removals = Removals;
    }
}
