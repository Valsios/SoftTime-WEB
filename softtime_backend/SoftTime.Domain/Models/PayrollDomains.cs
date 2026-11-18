namespace SoftTime.Domain.Models;

/// <summary>
/// Domaines d'un envoi en paie (T_PAIE_ENVOI.DOMAINE, T_PAIE_INSTANTANE.DOMAINE, migration 011) : les variables d'heures,
/// et les cumuls cantine et frais de déplacement écrits dans T_CUMSAL (base STANDARD seulement).
/// </summary>
public static class PayrollDomains
{
    public const string Heures = "HEURES";
    public const string Cantine = "CANTINE";
    public const string Frais = "FRAIS";

    /// <summary>Domaines optionnels : exigés à la clôture s'ils sont activés pour la base, ou déjà envoyés sur la période.</summary>
    public static IReadOnlyList<string> Optionnels { get; } = new[] { Cantine, Frais };

    public static IReadOnlyList<string> Tous { get; } = new[] { Heures, Cantine, Frais };

    /// <summary>Unité des lignes figées de la cantine et des frais : « N » (nombre de repas, nombre de pointages).</summary>
    public const string UniteNombre = "N";

    /// <summary>Libellé en français (messages et écran).</summary>
    public static string Libelle(string? domaine) => domaine switch
    {
        Heures => "Heures",
        Cantine => "Cantine",
        Frais => "Frais de déplacement",
        _ => domaine ?? string.Empty
    };

    /// <summary>Version du format d'empreinte de l'instantané d'un domaine : 1 pour les heures (inchangé), 2 sinon.</summary>
    public static int VersionEmpreinte(string? domaine) =>
        domaine == Heures ? PayrollSnapshotFingerprint.Version : PayrollSnapshotFingerprint.VersionDomaine;
}
