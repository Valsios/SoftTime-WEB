namespace SoftTime.Application.Abstractions.Payroll;

/// <summary>Filtre de la sélection d'un envoi en paie. Un envoi filtré ne peut pas servir à clôturer la période.</summary>
public sealed record PayrollSendFilter(string? Departement, string? MatriculeDe, string? MatriculeA)
{
    public static PayrollSendFilter Aucun { get; } = new(null, null, null);

    public bool AvecFiltre =>
        !string.IsNullOrWhiteSpace(Departement) || !string.IsNullOrWhiteSpace(MatriculeDe) || !string.IsNullOrWhiteSpace(MatriculeA);

    public static PayrollSendFilter De(string? departement, string? matriculeDe, string? matriculeA) =>
        new(Nettoyer(departement), Nettoyer(matriculeDe), Nettoyer(matriculeA));

    private static string? Nettoyer(string? valeur) => string.IsNullOrWhiteSpace(valeur) ? null : valeur.Trim();
}

/// <summary>
/// Instantané à figer avec un envoi en paie : les valeurs RÉELLEMENT écrites (une mise à jour qui n'a modifié aucune ligne
/// n'y figure pas, elle est seulement comptée dans <see cref="SansLigne"/>), le filtre et les options de l'envoi.
/// </summary>
public sealed record PayrollSnapshotDraft(
    IReadOnlyList<PayrollSnapshotLine> Lignes,
    int SansLigne,
    int NonRemis,
    PayrollSendFilter Filtre,
    bool RemiseAZero,
    decimal HeuresParJour);

/// <summary>Collecte des lignes écrites pendant un envoi (writers STANDARD et AUTRE).</summary>
public sealed class PayrollSnapshotBuilder
{
    private readonly List<PayrollSnapshotLine> _lignes = new();

    public IReadOnlyList<PayrollSnapshotLine> Lignes => _lignes;

    /// <summary>Ajoute une valeur écrite (à appeler seulement quand au moins une ligne de paie a été modifiée).</summary>
    public void Ecrite(string? matricule, int? numSal, string variable, string destination, string unite, decimal valeur, bool remiseAZero) =>
        _lignes.Add(new PayrollSnapshotLine(matricule ?? string.Empty, numSal, variable, destination, unite, valeur, remiseAZero).Normalisee());

    public PayrollSnapshotDraft Construire(int sansLigne, int nonRemis, PayrollSendFilter filtre, PayrollVariableSelection selection) =>
        new(_lignes.ToList(), sansLigne, nonRemis, filtre, selection.RemiseAZeroSansValeur, selection.HeuresParJour);
}
