namespace SoftTime.Domain.Entities.SoftTime;

public class HsValidee
{
    public int Id { get; set; }

    public string? BddSage { get; set; }

    public string? BddPointage { get; set; }

    public string? Matricules { get; set; }

    public DateTime? DateDeb { get; set; }

    public DateTime? DateFin { get; set; }

    public decimal? Ht { get; set; }

    public decimal? Hs { get; set; }

    public decimal? Nuit { get; set; }

    public decimal? Dimanche { get; set; }

    public decimal? Feries { get; set; }

    public bool? ValidationHs { get; set; }

    public decimal? Retard { get; set; }

    public decimal? Absence { get; set; }
}
