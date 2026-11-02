using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;

namespace SoftTime.Domain.Services;

/// <summary>
/// Index des pointages pour les règles de clôture, construit UNE fois : pointages exploitables
/// (même filtre que le pré-traitement) regroupés par clé matricule, triés (date, heure), avec le
/// nombre de pointages par (matricule, jour). La clé canonique n'est calculée qu'une fois par pointage.
/// </summary>
public sealed class ClosingPunchIndex
{
    private static readonly IReadOnlyList<PunchMoment> Aucun = Array.Empty<PunchMoment>();

    private readonly Dictionary<string, IReadOnlyList<PunchMoment>> _momentsParCle;
    private readonly Dictionary<(string Cle, DateTime Jour), int> _nombreParJour;

    private ClosingPunchIndex(
        Dictionary<string, IReadOnlyList<PunchMoment>> momentsParCle,
        Dictionary<(string Cle, DateTime Jour), int> nombreParJour)
    {
        _momentsParCle = momentsParCle;
        _nombreParJour = nombreParJour;
    }

    public static ClosingPunchIndex Build(IEnumerable<T_POINTAGE> pointages)
    {
        var listes = new Dictionary<string, List<PunchMoment>>(StringComparer.Ordinal);
        var nombreParJour = new Dictionary<(string Cle, DateTime Jour), int>();
        foreach (var p in pointages)
        {
            if (p.DATE_POINTAGE is not DateTime date || p.HEURE_POINTAGE is not TimeSpan heure
                || string.IsNullOrWhiteSpace(p.MATRICULE_SAGE))
                continue;

            var cle = MatriculeIdentity.Cle(p.MATRICULE_SAGE);
            var jour = date.Date;
            if (!listes.TryGetValue(cle, out var liste))
            {
                liste = new List<PunchMoment>();
                listes[cle] = liste;
            }
            liste.Add(new PunchMoment(jour, heure));
            nombreParJour[(cle, jour)] = nombreParJour.GetValueOrDefault((cle, jour)) + 1;
        }

        var momentsParCle = new Dictionary<string, IReadOnlyList<PunchMoment>>(listes.Count, StringComparer.Ordinal);
        foreach (var (cle, liste) in listes)
            momentsParCle[cle] = liste.OrderBy(m => m.Date).ThenBy(m => m.Heure).ToList();

        return new ClosingPunchIndex(momentsParCle, nombreParJour);
    }

    /// <summary>Clés matricule ayant au moins un pointage.</summary>
    public IEnumerable<string> Cles => _momentsParCle.Keys;

    /// <summary>Pointages d'un matricule, triés par date puis heure (vide si aucun).</summary>
    public IReadOnlyList<PunchMoment> Moments(string cle) => _momentsParCle.GetValueOrDefault(cle) ?? Aucun;

    public bool APointage(string cle, DateTime jour) => _nombreParJour.ContainsKey((cle, jour.Date));

    /// <summary>Couples (matricule, jour) pointés, avec leur nombre de pointages.</summary>
    public IEnumerable<(string Cle, DateTime Jour, int Nombre)> Jours =>
        _nombreParJour.Select(kv => (kv.Key.Cle, kv.Key.Jour, kv.Value));
}
