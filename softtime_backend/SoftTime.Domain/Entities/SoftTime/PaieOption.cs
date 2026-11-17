namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// T_PAIE_OPTION : option de paie d'une base SAGE (une ligne par base et par clé). Une base sans ligne pour une clé
/// garde la valeur par défaut de cette option (voir <see cref="PayrollOptions"/>).
/// </summary>
public class PaieOption
{
    public int Id { get; set; }

    public string BddSage { get; set; } = string.Empty;

    /// <summary>Clé stable de l'option (voir <see cref="PayrollOptions"/>).</summary>
    public string Cle { get; set; } = string.Empty;

    /// <summary>Valeur texte : « 1 » ou « 0 » pour une option booléenne.</summary>
    public string Valeur { get; set; } = string.Empty;

    public DateTime? ModifieLe { get; set; }

    public string? ModifiePar { get; set; }
}

/// <summary>Clés des options de paie (T_PAIE_OPTION.CLE) et lecture de leur valeur.</summary>
public static class PayrollOptions
{
    /// <summary>À l'envoi, remettre à 0 les salariés du périmètre sans valeur (variables cochées). Défaut : vrai.</summary>
    public const string RemiseAZeroSansValeur = "RemiseAZeroSansValeur";

    public const string Vrai = "1";
    public const string Faux = "0";

    public static string Texte(bool valeur) => valeur ? Vrai : Faux;

    /// <summary>Valeur booléenne d'une option ; absente ou illisible : la valeur par défaut.</summary>
    public static bool Booleen(IEnumerable<PaieOption> options, string cle, bool parDefaut)
    {
        var ligne = options.FirstOrDefault(o => string.Equals(o.Cle, cle, StringComparison.OrdinalIgnoreCase));
        return ligne?.Valeur?.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ when ligne?.Valeur is not null && bool.TryParse(ligne.Valeur.Trim(), out var b) => b,
            _ => parDefaut,
        };
    }
}
