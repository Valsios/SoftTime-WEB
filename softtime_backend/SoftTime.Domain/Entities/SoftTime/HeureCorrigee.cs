namespace SoftTime.Domain.Entities.SoftTime;

public class HeureCorrigee
{
    public decimal Id { get; set; }

    public string? BddSage { get; set; }

    public string? BddPointeuse { get; set; }

    public string? MatriculeSage { get; set; }

    public DateTime? DatePointageIn { get; set; }

    public DateTime? DatePointageOut { get; set; }

    public TimeSpan? HEntree { get; set; }

    public TimeSpan? Hps { get; set; }

    public TimeSpan? Hpe { get; set; }

    public TimeSpan? HSortie { get; set; }

    public bool? AbsencePm { get; set; }

    public bool? AbsenceAm { get; set; }

    public string? IntituleAbsence { get; set; }

    public TimeSpan? Retard { get; set; }

    public bool? Ferier { get; set; }

    public bool? FerierAm { get; set; }

    public bool? FerierPm { get; set; }

    public bool? ImportationSage { get; set; }

    public DateTime? DateImportationSage { get; set; }

    public bool? ValiderCorrection { get; set; }

    public decimal? Hs { get; set; }

    public decimal? MNuit { get; set; }

    public decimal? MDimanche { get; set; }

    public decimal? MFeries { get; set; }

    public bool? ValiderHs { get; set; }

    /// <summary>
    /// Origine de la ligne : <c>AUTO</c> (pré-traitement des pointages) ou <c>MANUEL</c>
    /// (saisie ou import Excel). NULL = ligne héritée, jamais écrasée par le pré-traitement.
    /// Colonne ajoutée de façon idempotente par <c>PunchProcessingService</c>.
    /// </summary>
    public string? Origine { get; set; }
}
