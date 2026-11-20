using System.Globalization;

using SoftTime.Domain.Models;

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

    /// <summary>Absence envoyée en jours : nombre d'heures d'une journée (texte invariant, « 8 », « 7.5 »). Défaut : 8.</summary>
    public const string AbsenceHeuresParJour = "AbsenceHeuresParJour";

    /// <summary>
    /// Codes d'absence comptés comme non justifiés (lot C3) : présence de la ligne = réglage enregistré pour la base ;
    /// valeur « 1 » ou « 0 » = l'arbitrage sans code est compté. Absente : <see cref="AbsenceCodesComptes.Default"/>.
    /// </summary>
    public const string AbsenceSansCodeCompte = "AbsenceSansCodeCompte";

    /// <summary>
    /// Préfixe des codes comptés : une ligne par code (<c>AbsenceCodeCompte.1</c>, <c>.2</c>…), valeur = libellé. Une ligne
    /// par code plutôt qu'une liste dans une seule valeur : <c>VALEUR</c> est limitée à 256 caractères et la liste complète
    /// des codes d'une base peut la dépasser.
    /// </summary>
    public const string PrefixeAbsenceCodeCompte = "AbsenceCodeCompte.";

    /// <summary>Clé de la ligne du code compté de rang <paramref name="rang"/> (à partir de 1).</summary>
    public static string CleAbsenceCode(int rang) => PrefixeAbsenceCodeCompte + rang.ToString(CultureInfo.InvariantCulture);

    public const decimal HeuresParJourParDefaut = 8m;
    public const decimal HeuresParJourMin = 1m;
    public const decimal HeuresParJourMax = 24m;

    public const string Vrai = "1";
    public const string Faux = "0";

    public static string Texte(bool valeur) => valeur ? Vrai : Faux;

    /// <summary>
    /// Domaine CANTINE envoyé à la paie par cette base (lot D5) : « 1 » l'exige à la clôture. Défaut : désactivé (« 0 »).
    /// </summary>
    public const string DomaineCantineActif = "DomaineCantineActif";

    /// <summary>Domaine FRAIS (frais de déplacement) envoyé à la paie par cette base : « 1 » l'exige à la clôture. Défaut : « 0 ».</summary>
    public const string DomaineFraisActif = "DomaineFraisActif";

    /// <summary>
    /// Code de la constante SAGE qui porte le cumul de la cantine (lot D6). Absente : <see cref="CodeConstanteCantineParDefaut"/>,
    /// le code historique du desktop, qui dépasse la longueur de <c>T_CST.CodeConstante</c> (10 caractères) et n'existe donc
    /// dans aucune base SAGE : il faut créer la constante dans SAGE puis saisir son code.
    /// </summary>
    public const string CodeConstanteCantine = "CodeConstanteCantine";

    /// <summary>Code historique (desktop) de la constante de la cantine, utilisé tant qu'aucun code n'est configuré.</summary>
    public const string CodeConstanteCantineParDefaut = "CANTINE_CODE";

    /// <summary>Code de constante de la cantine de la base (configuré, sinon le code historique).</summary>
    public static string CodeCantine(IEnumerable<PaieOption> options)
    {
        var valeur = options.FirstOrDefault(o => string.Equals(o.Cle, CodeConstanteCantine, StringComparison.OrdinalIgnoreCase))?.Valeur?.Trim();
        return string.IsNullOrEmpty(valeur) ? CodeConstanteCantineParDefaut : valeur;
    }

    /// <summary>Clé d'activation d'un domaine optionnel (CANTINE, FRAIS).</summary>
    public static string CleDomaine(string domaine) => domaine switch
    {
        PayrollDomains.Cantine => DomaineCantineActif,
        PayrollDomains.Frais => DomaineFraisActif,
        _ => throw new DomainException($"Le domaine {domaine} ne s'active pas : seuls la cantine et les frais sont optionnels.")
    };

    /// <summary>Domaine optionnel activé pour la base ; sans ligne : désactivé.</summary>
    public static bool DomaineActif(IEnumerable<PaieOption> options, string domaine) => Booleen(options, CleDomaine(domaine), false);

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

    /// <summary>Heures par jour de l'Absence envoyée en jours ; absente ou illisible : 8.</summary>
    public static decimal HeuresParJour(IEnumerable<PaieOption> options)
    {
        var ligne = options.FirstOrDefault(o => string.Equals(o.Cle, AbsenceHeuresParJour, StringComparison.OrdinalIgnoreCase));
        return ligne is not null
               && decimal.TryParse(ligne.Valeur?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var valeur)
               && HeuresParJourValide(valeur)
            ? valeur
            : HeuresParJourParDefaut;
    }

    /// <summary>Entre 1 et 24, une décimale au plus.</summary>
    public static bool HeuresParJourValide(decimal valeur) =>
        valeur >= HeuresParJourMin && valeur <= HeuresParJourMax && decimal.Round(valeur, 1) == valeur;

    /// <summary>Texte stocké d'un nombre d'heures par jour (« 8 », « 7.5 »).</summary>
    public static string TexteHeures(decimal valeur) => valeur.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Codes d'absence comptés de la base ; sans réglage enregistré : la valeur par défaut.</summary>
    public static AbsenceCodesComptes AbsenceCodes(IEnumerable<PaieOption> options)
    {
        var liste = options.ToList();
        var marqueur = liste.FirstOrDefault(o => string.Equals(o.Cle, AbsenceSansCodeCompte, StringComparison.OrdinalIgnoreCase));
        if (marqueur is null)
            return AbsenceCodesComptes.Default;
        var codes = liste
            .Where(o => o.Cle.StartsWith(PrefixeAbsenceCodeCompte, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(o.Valeur))
            .OrderBy(o => int.TryParse(o.Cle[PrefixeAbsenceCodeCompte.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rang) ? rang : int.MaxValue)
            .Select(o => o.Valeur.Trim())
            .ToList();
        return new AbsenceCodesComptes(codes, Booleen([marqueur], AbsenceSansCodeCompte, AbsenceCodesComptes.Default.SansCode));
    }
}
