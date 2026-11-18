namespace SoftTime.Domain.Entities.SoftTime;

/// <summary>
/// T_PAIE_INSTANTANE : entête de l'instantané d'un envoi en paie (une ligne par envoi réussi de T_PAIE_ENVOI). Les lignes
/// (<see cref="PaieInstantaneLigne"/>) sont exactement les valeurs réellement écrites dans la paie. Immuable : aucun chemin
/// de modification dans le code, et la base refuse toute mise à jour ou suppression (déclencheurs de la migration 009).
/// </summary>
public class PaieInstantane
{
    public int Id { get; set; }

    /// <summary>Envoi (T_PAIE_ENVOI.ID) dont c'est l'instantané : un seul instantané par envoi.</summary>
    public int EnvoiId { get; set; }

    public string BddSage { get; set; } = string.Empty;

    public DateTime PeriodeDebut { get; set; }

    public DateTime PeriodeFin { get; set; }

    /// <summary>« STANDARD » ou « AUTRE ».</summary>
    public string TypeBase { get; set; } = string.Empty;

    /// <summary>Date UTC de l'envoi.</summary>
    public DateTime EnvoyeLe { get; set; }

    public string? EnvoyePar { get; set; }

    /// <summary>Filtre département de l'envoi (null : aucun).</summary>
    public string? Departement { get; set; }

    /// <summary>Filtre « matricule de » de l'envoi (null : aucun).</summary>
    public string? MatriculeDe { get; set; }

    /// <summary>Filtre « matricule à » de l'envoi (null : aucun).</summary>
    public string? MatriculeA { get; set; }

    /// <summary>Vrai si l'envoi portait un filtre (département ou matricules) : il ne peut pas servir à clôturer.</summary>
    public bool AvecFiltre { get; set; }

    /// <summary>Option « remettre à 0 les salariés sans valeur » au moment de l'envoi.</summary>
    public bool RemiseAZero { get; set; }

    /// <summary>Heures par jour de la base au moment de l'envoi (conversion de l'Absence en jours).</summary>
    public decimal HeuresParJour { get; set; }

    /// <summary>Version du format canonique de l'empreinte (<c>PayrollSnapshotFingerprint.Version</c>).</summary>
    public int VersionFormat { get; set; }

    /// <summary>SHA-256 (64 caractères hexadécimaux minuscules) de la représentation canonique des lignes.</summary>
    public string Empreinte { get; set; } = string.Empty;

    /// <summary>Nombre de lignes de l'instantané (valeurs réellement écrites).</summary>
    public int NbLignes { get; set; }

    /// <summary>Mises à jour tentées qui n'ont modifié aucune ligne de paie (cumul absent) : comptées, pas figées.</summary>
    public int NbSansLigne { get; set; }

    /// <summary>Salariés du périmètre sans valeur qui n'ont pas pu être remis à 0 (numéro de salarié introuvable).</summary>
    public int NbNonRemis { get; set; }

    /// <summary>Date UTC d'écriture de l'instantané.</summary>
    public DateTime CreeLe { get; set; }
}

/// <summary>T_PAIE_INSTANTANE_LIGNE : une valeur réellement écrite dans la paie par un envoi. Immuable.</summary>
public class PaieInstantaneLigne
{
    public long Id { get; set; }

    public int InstantaneId { get; set; }

    public string Matricule { get; set; } = string.Empty;

    /// <summary>Numéro de salarié SAGE (base STANDARD) ; null pour une base AUTRE.</summary>
    public int? NumSal { get; set; }

    /// <summary>Nom canonique de la variable (Exo130… Absence).</summary>
    public string Variable { get; set; } = string.Empty;

    /// <summary>Destination : code de constante SAGE (STANDARD) ou colonne de la table de paie (AUTRE).</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>Unité d'envoi : « H » (heures) ou « J » (jours).</summary>
    public string Unite { get; set; } = string.Empty;

    /// <summary>Valeur écrite, arrondie à 4 décimales.</summary>
    public decimal Valeur { get; set; }

    /// <summary>Vrai pour un zéro de la remise à 0 (salarié du périmètre sans ligne de valeurs).</summary>
    public bool RemiseAZero { get; set; }
}
