namespace SoftTime.Domain.Entities.SoftTime;

public class PaieEcriture
{
    public int Id { get; set; }

    public string BddSage { get; set; } = string.Empty;

    public string TableCible { get; set; } = string.Empty;

    public string ColMatricule { get; set; } = string.Empty;

    public string Categorie { get; set; } = string.Empty;

    public string ColValeur { get; set; } = string.Empty;
}
