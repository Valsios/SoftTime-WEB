using SoftTime.Application.Abstractions.Payroll;

namespace SoftTime.Application.Services;

/// <summary>Règles pures sur les colonnes de destination d'une base AUTRE.</summary>
public static class PayrollTargetColumns
{
    private static readonly HashSet<string> Entiers = new(StringComparer.OrdinalIgnoreCase) { "bigint", "int", "smallint", "tinyint" };
    private static readonly HashSet<string> Decimaux = new(StringComparer.OrdinalIgnoreCase) { "decimal", "numeric" };
    private static readonly HashSet<string> Flottants = new(StringComparer.OrdinalIgnoreCase) { "money", "smallmoney", "float", "real" };

    /// <summary>Type numérique accepté par l'écriture de paie AUTRE (mêmes types que son préflight).</summary>
    public static bool EstNumerique(string? type) =>
        type is not null && (Entiers.Contains(type) || Decimaux.Contains(type) || Flottants.Contains(type));

    /// <summary>
    /// La colonne peut-elle recevoir une valeur avec <paramref name="decimales"/> décimales ? Une colonne entière ne le
    /// peut pas ; un decimal/numeric doit avoir au moins autant de décimales ; money, float et real le peuvent.
    /// </summary>
    public static bool AccepteDecimales(PayrollTargetColumn colonne, int decimales)
    {
        if (Entiers.Contains(colonne.Type))
            return decimales == 0;
        if (Decimaux.Contains(colonne.Type))
            return (colonne.Echelle ?? 0) >= decimales;
        return Flottants.Contains(colonne.Type);
    }
}
