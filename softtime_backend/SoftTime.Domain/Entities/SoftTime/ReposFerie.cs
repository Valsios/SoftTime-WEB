namespace SoftTime.Domain.Entities.SoftTime;

public class ReposFerie
{
    public int Id { get; set; }

    public string? BddSage { get; set; }

    public string? BddPointeuse { get; set; }

    public string? MatriculeSage { get; set; }

    public DateTime? DateDebutSemaine { get; set; }

    public decimal? FerieDebutSemaine { get; set; }

    public bool? Etat { get; set; }
}
