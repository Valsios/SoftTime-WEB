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
/// </summary>
public static class PayrollSnapshotFingerprint
{
    public const int Version = 1;

    private const string Entete = "SOFTTIME-PAIE-INSTANTANE";

    public static decimal Arrondir(decimal valeur) => Math.Round(valeur, 4, MidpointRounding.AwayFromZero);

    /// <summary>Représentation canonique (exposée pour la documentation et les tests).</summary>
    public static string Canonique(
        int version, string baseSage, DateTime debut, DateTime fin, string typeBase, IEnumerable<PayrollSnapshotLine> lignes)
    {
        if (version != Version)
            throw new DomainException($"Format d'empreinte d'instantané inconnu : {version}.");
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
        var premiere = string.Join('|', Entete, version.ToString(CultureInfo.InvariantCulture), (baseSage ?? string.Empty).Trim(),
            debut.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), fin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            (typeBase ?? string.Empty).Trim().ToUpperInvariant());
        return string.Join('\n', new[] { premiere }.Concat(triees));
    }

    /// <summary>Empreinte au format courant.</summary>
    public static string Calculer(string baseSage, DateTime debut, DateTime fin, string typeBase, IEnumerable<PayrollSnapshotLine> lignes)
        => Calculer(Version, baseSage, debut, fin, typeBase, lignes);

    /// <summary>Empreinte dans une version donnée du format.</summary>
    public static string Calculer(int version, string baseSage, DateTime debut, DateTime fin, string typeBase, IEnumerable<PayrollSnapshotLine> lignes)
    {
        var octets = new UTF8Encoding(false).GetBytes(Canonique(version, baseSage, debut, fin, typeBase, lignes));
        return Convert.ToHexString(SHA256.HashData(octets)).ToLowerInvariant();
    }
}
