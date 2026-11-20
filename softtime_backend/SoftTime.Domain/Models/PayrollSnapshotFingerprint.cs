using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SoftTime.Domain.Models;

/// <summary>Ligne figée d'un envoi en paie, telle qu'elle entre dans l'empreinte.</summary>
public sealed record PayrollSnapshotLine(
    string Matricule, int? NumSal, string Variable, string Destination, string Unite, decimal Valeur, bool RemiseAZero)
{
    /// <summary>Ligne normalisée : textes sans espaces de bord, valeur arrondie à 4 décimales (précision de la colonne).</summary>
    public PayrollSnapshotLine Normalisee() => this with
    {
        Matricule = (Matricule ?? string.Empty).Trim(),
        Variable = (Variable ?? string.Empty).Trim(),
        Destination = (Destination ?? string.Empty).Trim(),
        Unite = (Unite ?? string.Empty).Trim(),
        Valeur = PayrollSnapshotFingerprint.Arrondir(Valeur)
    };
}

/// <summary>
/// Empreinte SHA-256 d'un instantané d'envoi en paie, sur une représentation canonique VERSIONNÉE (format 1) :
/// <list type="number">
///   <item>première ligne : <c>SOFTTIME-PAIE-INSTANTANE|1|{base}|{début aaaa-MM-jj}|{fin aaaa-MM-jj}|{type de base}</c> ;</item>
///   <item>puis une ligne par valeur : <c>{matricule}|{numéro de salarié ou vide}|{variable}|{destination}|{unité}|{valeur}|{0 ou 1}</c>,
///     la valeur en culture invariante, point décimal, exactement 4 décimales (arrondi au plus proche, milieu loin de zéro),
///     le dernier champ valant 1 pour un zéro de la remise à 0 ;</item>
///   <item>lignes triées en ordinal sur (matricule, variable, destination, numéro de salarié, unité, valeur, indicateur) ;</item>
///   <item>séparateur de champ « | », séparateur de ligne « \n » (aucun en fin), encodage UTF-8 sans BOM ;</item>
///   <item>résultat : 64 caractères hexadécimaux minuscules.</item>
/// </list>
/// Ni la date ni l'auteur de l'envoi n'entrent dans l'empreinte : deux envois identiques ont la même empreinte. Tout
/// changement de ce format exige une nouvelle version (les anciennes restent vérifiables avec leur version).
/// <para>
/// Le format 1 est celui des HEURES et ne change pas. Le format 2 (<see cref="VersionDomaine"/>, migration 011) est celui
/// des domaines CANTINE et FRAIS : seule la première ligne diffère, elle nomme le domaine après la version :
/// <c>SOFTTIME-PAIE-INSTANTANE|2|{domaine}|{base}|{début}|{fin}|{type de base}</c> ; les lignes de valeurs, leur tri, les
/// séparateurs, l'encodage et l'arrondi sont ceux du format 1. Un format qui ne correspond pas au domaine (1 hors HEURES,
/// 2 pour HEURES) est refusé : la vérification le compte comme une altération.
/// </para>
/// </summary>
public static class PayrollSnapshotFingerprint
{
    /// <summary>Format des heures (domaine HEURES), inchangé depuis la migration 009.</summary>
    public const int Version = 1;

    /// <summary>Format des domaines CANTINE et FRAIS (migration 011).</summary>
    public const int VersionDomaine = 2;

    private const string Entete = "SOFTTIME-PAIE-INSTANTANE";

    public static decimal Arrondir(decimal valeur) => Math.Round(valeur, 4, MidpointRounding.AwayFromZero);

    /// <summary>Représentation canonique des heures (exposée pour la documentation et les tests).</summary>
    public static string Canonique(
        int version, string baseSage, DateTime debut, DateTime fin, string typeBase, IEnumerable<PayrollSnapshotLine> lignes)
        => Canonique(version, PayrollDomains.Heures, baseSage, debut, fin, typeBase, lignes);

    /// <summary>Représentation canonique d'un domaine dans une version donnée (1 : HEURES ; 2 : CANTINE, FRAIS).</summary>
    public static string Canonique(
        int version, string domaine, string baseSage, DateTime debut, DateTime fin, string typeBase, IEnumerable<PayrollSnapshotLine> lignes)
    {
        var entete = version switch
        {
            Version when domaine == PayrollDomains.Heures => new[] { Entete, version.ToString(CultureInfo.InvariantCulture) },
            VersionDomaine when domaine is PayrollDomains.Cantine or PayrollDomains.Frais
                => new[] { Entete, version.ToString(CultureInfo.InvariantCulture), domaine },
            Version or VersionDomaine => throw new DomainException($"Format d'empreinte {version} incompatible avec le domaine {domaine}."),
            _ => throw new DomainException($"Format d'empreinte d'instantané inconnu : {version}.")
        };
        var triees = lignes
            .Select(l => l.Normalisee())
            .OrderBy(l => l.Matricule, StringComparer.Ordinal)
            .ThenBy(l => l.Variable, StringComparer.Ordinal)
            .ThenBy(l => l.Destination, StringComparer.Ordinal)
            .ThenBy(l => l.NumSal ?? int.MinValue)
            .ThenBy(l => l.Unite, StringComparer.Ordinal)
            .ThenBy(l => l.Valeur)
            .ThenBy(l => l.RemiseAZero)
            .Select(l => string.Join('|',
                l.Matricule,
                l.NumSal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                l.Variable,
                l.Destination,
                l.Unite,
                l.Valeur.ToString("F4", CultureInfo.InvariantCulture),
                l.RemiseAZero ? "1" : "0"));
        var premiere = string.Join('|', entete.Concat(new[] { (baseSage ?? string.Empty).Trim(),
            debut.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), fin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            (typeBase ?? string.Empty).Trim().ToUpperInvariant() }));
        return string.Join('\n', new[] { premiere }.Concat(triees));
    }

    /// <summary>Empreinte au format courant.</summary>
    public static string Calculer(string baseSage, DateTime debut, DateTime fin, string typeBase, IEnumerable<PayrollSnapshotLine> lignes)
        => Calculer(Version, baseSage, debut, fin, typeBase, lignes);

    /// <summary>Empreinte des heures dans une version donnée du format.</summary>
    public static string Calculer(int version, string baseSage, DateTime debut, DateTime fin, string typeBase, IEnumerable<PayrollSnapshotLine> lignes)
        => Calculer(version, PayrollDomains.Heures, baseSage, debut, fin, typeBase, lignes);

    /// <summary>Empreinte d'un domaine dans une version donnée du format.</summary>
    public static string Calculer(
        int version, string domaine, string baseSage, DateTime debut, DateTime fin, string typeBase, IEnumerable<PayrollSnapshotLine> lignes)
    {
        var octets = new UTF8Encoding(false).GetBytes(Canonique(version, domaine, baseSage, debut, fin, typeBase, lignes));
        return Convert.ToHexString(SHA256.HashData(octets)).ToLowerInvariant();
    }

    /// <summary>Empreinte d'un domaine dans son format courant (1 pour les heures, 2 pour la cantine et les frais).</summary>
    public static string CalculerDomaine(string domaine, string baseSage, DateTime debut, DateTime fin, string typeBase, IEnumerable<PayrollSnapshotLine> lignes)
        => Calculer(PayrollDomains.VersionEmpreinte(domaine), domaine, baseSage, debut, fin, typeBase, lignes);
}
