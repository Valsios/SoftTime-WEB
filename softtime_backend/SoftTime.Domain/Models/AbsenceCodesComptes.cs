using System.Globalization;
using System.Text;

namespace SoftTime.Domain.Models;

/// <summary>
/// Réglage par base des codes d'absence comptés comme non justifiés dans l'Absence de la page Heures supplémentaires
/// (lot C3). Un jour planifié sans aucune ligne d'heure corrigée est toujours compté ; une ligne arbitrée en absence
/// (<c>ABSENCE_AM</c> / <c>ABSENCE_PM</c>, sans heure) n'est comptée que si son code (<c>INTITULE_ABSENCE</c>) figure
/// dans la liste, ou, sans code, si <see cref="SansCode"/> est vrai.
///
/// La correspondance se fait sur le libellé, de façon tolérante : casse, accents et espaces en trop ignorés.
/// </summary>
/// <param name="Codes">Libellés des codes comptés, tels qu'enregistrés.</param>
/// <param name="SansCode">Un arbitrage sans code est compté.</param>
public sealed record AbsenceCodesComptes(IReadOnlyList<string> Codes, bool SansCode)
{
    /// <summary>Libellé du code compté par défaut.</summary>
    public const string AbsenceNonJustifiee = "ABSENCE NON JUSTIFIEE";

    /// <summary>Sans réglage pour la base : « ABSENCE NON JUSTIFIEE » et l'arbitrage sans code.</summary>
    public static AbsenceCodesComptes Default { get; } = new([AbsenceNonJustifiee], true);

    /// <summary>Une ligne arbitrée portant ce code est-elle comptée dans l'Absence ?</summary>
    public bool Compte(string? intitule)
    {
        var cle = Normaliser(intitule);
        return cle.Length == 0 ? SansCode : Codes.Any(c => Normaliser(c) == cle);
    }

    /// <summary>Même réglage (mêmes codes à la normalisation près, même choix pour l'arbitrage sans code) ?</summary>
    public bool Equivaut(AbsenceCodesComptes autre) =>
        SansCode == autre.SansCode
        && Cles().SetEquals(autre.Cles());

    private HashSet<string> Cles() => Codes.Select(Normaliser).Where(c => c.Length > 0).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Forme comparable d'un libellé : espaces en trop retirés (début, fin et répétés), accents retirés, majuscules.
    /// Même traitement des accents que la recherche (<c>SearchText</c>).
    /// </summary>
    public static string Normaliser(string? libelle)
    {
        if (string.IsNullOrWhiteSpace(libelle))
            return string.Empty;
        var decompose = libelle.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decompose.Length);
        var espace = false;
        foreach (var c in decompose)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsWhiteSpace(c))
            {
                espace = true;
                continue;
            }
            if (espace && sb.Length > 0)
                sb.Append(' ');
            espace = false;
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }
}
