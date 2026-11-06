namespace SoftTime.Domain.Entities.SoftTime;

public class Categorie
{
    public int IdCategorie { get; set; }

    public string? Intitule { get; set; }

    public int IdCardPaie { get; set; }

    public TimeSpan? Pause { get; set; }

    public decimal? HeureSemaine { get; set; }

    public string? BddSage { get; set; }

    public string? BddPointeuse { get; set; }
}
